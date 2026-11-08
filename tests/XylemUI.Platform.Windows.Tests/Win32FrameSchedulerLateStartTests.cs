using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// When a frame that the swap chain's object signaled for runs (design §2.11, "Frames that start late"). An event stands in for
// the object, a fake reader for its statistics, and a fake clock for the counter; the test runs the frames itself, so nothing
// waits on the late start's timer. An animation asks for each next frame inside the frame before, as WindowFrames does.
[Collection(UiThreadGroup.Name)]
public sealed unsafe class Win32FrameSchedulerLateStartTests(UiThreadFixture ui)
{
    private static readonly double Tick = Stopwatch.Frequency / 1000.0;

    /// <summary>
    /// The wait runs until the margin before the first refresh after now, also when the statistics are ahead of now, and is not
    /// positive when that start is past.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A refresh at 1,000 ms, a period of 16 ms, and a margin of 4 ms.
    ///
    /// Steps:
    /// - Compute the wait at 1,001 ms, at 1,014 ms, at 1,040 ms, and at 999 ms.
    ///
    /// Expected:
    /// - 11 ms (to 1,012 ms), −2 ms (that start is past), 4 ms (to 1,044 ms), and −3 ms (the refresh at 1,000 ms is the next one).
    /// </remarks>
    [Fact]
    public void WaitUntilLateStart_IsToTheMarginBeforeTheNextRefresh()
    {
        static double Wait(double now) => Win32FrameScheduler.WaitUntilLateStart((long)(now * Tick), (long)(1000 * Tick), 16 * Tick, 4 * Tick) / Tick;

        Assert.Equal(11, Wait(1001), 3);
        Assert.Equal(-2, Wait(1014), 3);
        Assert.Equal(4, Wait(1040), 3);
        Assert.Equal(-3, Wait(999), 3);
    }

    /// <summary>
    /// In an animation, a signal with fresh statistics does not run the frame: the loop waits on the late start's timer with no
    /// timeout, and the timer's wake runs the frame and goes back to the swap chain's object.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose clock is an event, with a reader whose last refresh is 1 ms before now, and an animation started.
    ///
    /// Steps:
    /// - Give the scheduler a signaled wait for the animation's next frame.
    /// - Give it the late start timer's wake.
    ///
    /// Expected:
    /// - After the signal, the frame has not run, a frame is pending, and the loop waits on a handle other than the object with
    ///   no timeout.
    /// - After the timer, the frame has run, one late start is counted, and the loop waits on the object again.
    /// </remarks>
    [Fact]
    public void Signal_InAnAnimation_WaitsForTheLateStart() =>
        Run(
            (scheduler, clock, refresh, animation) =>
            {
                animation.Start();
                Assert.Equal(1, animation.Runs);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((1, true), (animation.Runs, scheduler.HasPendingFrame));
                Assert.NotEqual(clock, scheduler.WaitHandle);
                Assert.Equal(INFINITE, scheduler.WaitTimeoutMilliseconds);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal((2, 1L), (animation.Runs, scheduler.LateStarts));
                Assert.Equal(clock, scheduler.WaitHandle);
            }
        );

    /// <summary>
    /// A frame asked for while none was pending, outside a callback (a key on a static screen), runs on its signal at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose clock is an event, with fresh statistics.
    ///
    /// Steps:
    /// - Ask for a frame from outside any callback, and give a signal; do it again.
    ///
    /// Expected:
    /// - Each frame runs on its signal, and no late start is counted.
    /// </remarks>
    [Fact]
    public void Signal_ForAFrameAskedFromIdle_RunsAtOnce() =>
        Run(
            (scheduler, clock, refresh, animation) =>
            {
                for (int i = 1; i <= 2; i++)
                {
                    scheduler.RequestFrame(animation.Frame);
                    animation.Stop();
                    scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                    Assert.Equal(i, animation.Runs);
                }

                Assert.Equal(0L, scheduler.LateStarts);
            }
        );

    /// <summary>
    /// In an animation, a signal runs the frame at once after a callback longer than 2 ms, with statistics older than 1 s, with
    /// no statistics, and when the late start is off; a timeout runs it at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose clock is an event, with a reader whose last refresh is 1 ms before now unless a step changes it, and an
    ///   animation started.
    ///
    /// Steps:
    /// - Make the next frame move the fake clock 3 ms, and run it with a signal and the late start's timer.
    /// - Give a signal for the next frame.
    /// - Make the last refresh 1.5 s old, and give a signal.
    /// - Make the reader return no statistics, and give a signal.
    /// - Turn the late start off with fresh statistics, and give a signal.
    /// - Give a wait that timed out.
    ///
    /// Expected:
    /// - Each frame after the slow one runs at once; only the slow one started late.
    /// </remarks>
    [Fact]
    public void Signal_RunsAtOnce_WhenTheLateStartDoesNotApply() =>
        Run(
            (scheduler, clock, refresh, animation) =>
            {
                animation.Start();
                animation.Body = () =>
                {
                    refresh.Now += 3;
                    refresh.Last = refresh.Now - 1;
                };
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                animation.Body = null;
                Assert.Equal(2, animation.Runs);

                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal(3, animation.Runs);

                refresh.Last = refresh.Now - 1500;
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal(4, animation.Runs);

                refresh.Last = refresh.Now - 1;
                refresh.Readable = false;
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.Equal(5, animation.Runs);

                refresh.Readable = true;
                Win32FrameScheduler.LateStartEnabled = false;
                try
                {
                    scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                    Assert.Equal(6, animation.Runs);
                }
                finally
                {
                    Win32FrameScheduler.LateStartEnabled = true;
                }

                scheduler.RunFrame(WAIT.WAIT_TIMEOUT);
                Assert.Equal((7, 1L), (animation.Runs, scheduler.LateStarts));
            }
        );

    /// <summary>
    /// The period is derived from two refreshes 30 apart, and the scheduler anchors again when the count goes back or the period
    /// is out of range.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose clock is an event, with a reader of refreshes every 16.6662 ms, the first numbered 1,000, and an
    ///   animation started.
    ///
    /// Steps:
    /// - Read refresh 1,000, then 1,029: one short of 30 apart.
    /// - Read 1,030.
    /// - Read refresh 500 (the count went back), 200 ms later; then 529 and 530 every 16.6662 ms from there.
    /// - Read 560 with a time that gives more than 50 ms a refresh from the anchor at 500, then 590 at 17 ms a refresh from 560.
    ///
    /// Expected:
    /// - No period after 1,029; 16.6662 ms after 1,030.
    /// - After the count went back, the period stays 16.6662 ms until 530 gives it again from the new anchor.
    /// - The out-of-range period is not taken, and the next is derived from the anchor at 560.
    /// </remarks>
    [Fact]
    public void Period_IsDerivedAndAnchoredAgain() =>
        Run(
            (scheduler, clock, refresh, animation) =>
            {
                const double period = 16.6662;
                void Read(uint count, double time)
                {
                    refresh.Count = count;
                    refresh.Last = time;
                    refresh.Now = time + 1;
                    scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                    scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                }

                animation.Start();
                Read(1000, 10_000);
                Read(1029, 10_000 + (29 * period));
                Assert.Equal(0, scheduler.DerivedPeriodMilliseconds);
                Read(1030, 10_000 + (30 * period));
                Assert.Equal(period, scheduler.DerivedPeriodMilliseconds, 3);

                double back = 10_000 + (30 * period) + 200;
                Read(500, back);
                Read(529, back + (29 * period));
                Read(530, back + (30 * period));
                Assert.Equal(period, scheduler.DerivedPeriodMilliseconds, 3);

                double far = back + (30 * period) + (30 * 120);
                Read(560, far);
                Assert.Equal(period, scheduler.DerivedPeriodMilliseconds, 3);
                Read(590, far + (30 * 17.0));
                Assert.Equal(17.0, scheduler.DerivedPeriodMilliseconds, 3);
            }
        );

    /// <summary>
    /// A frame run on the synchronous resize path while the late start waits takes that wait's signal, and a cancel ends the
    /// wait.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A scheduler whose clock is an event, with fresh statistics, and an animation started.
    ///
    /// Steps:
    /// - Give a signal, so that the next frame waits on the late start, and run the pending frame with no wait (the synchronous
    ///   resize path).
    /// - Give a signal again, so that the frame that one asked for waits on the late start, and cancel it.
    ///
    /// Expected:
    /// - After the resize path's frame, the frame has run, and the loop waits on the object again.
    /// - After the cancel, nothing is pending and a new request waits on the object.
    /// </remarks>
    [Fact]
    public void LateStart_EndsOnTheResizePathAndOnCancel() =>
        Run(
            (scheduler, clock, refresh, animation) =>
            {
                animation.Start();
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.NotEqual(clock, scheduler.WaitHandle);
                scheduler.RunFrame();
                Assert.Equal(2, animation.Runs);
                Assert.Equal(clock, scheduler.WaitHandle);

                // The resize path's present used the signal the late start took, so the next signal starts late again.
                scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
                Assert.NotEqual(clock, scheduler.WaitHandle);
                scheduler.CancelFrame();
                Assert.False(scheduler.HasPendingFrame);
                animation.Stop();
                scheduler.RequestFrame(animation.Frame);
                Assert.Equal((clock, 2), (scheduler.WaitHandle, animation.Runs));
            }
        );

    private void Run(Action<Win32FrameScheduler, HANDLE, FakeRefresh, Animation> test) =>
        ui.Invoke(() =>
        {
            HANDLE clock = CreateEventW(null, BOOL.TRUE, BOOL.TRUE, null);
            Assert.NotEqual(HANDLE.NULL, clock);
            using var scheduler = new Win32FrameScheduler();
            var refresh = new FakeRefresh();
            try
            {
                scheduler.Now = refresh.ReadNow;
                scheduler.SetFrameClock(clock, refresh.Read, 60);
                test(scheduler, clock, refresh, new Animation(scheduler));
            }
            finally
            {
                scheduler.CancelFrame();
                _ = CloseHandle(clock);
            }
        });

    // The statistics and the counter, in milliseconds: the last displayed refresh is 1 ms before now unless a test moves it.
    private sealed class FakeRefresh
    {
        public double Now { get; set; } = 10_000;

        public double Last { get; set; } = 9_999;

        public uint Count { get; set; } = 100;

        public bool Readable { get; set; } = true;

        public long ReadNow() => (long)(Now * Tick);

        public bool Read(out long qpcTime, out uint refreshCount)
        {
            qpcTime = Readable ? (long)(Last * Tick) : 0;
            refreshCount = Count;
            return Readable;
        }
    }

    // Frames that present and, while the animation runs, ask for the next one inside themselves.
    private sealed class Animation
    {
        private readonly Win32FrameScheduler _scheduler;
        private bool _running;

        public Animation(Win32FrameScheduler scheduler)
        {
            _scheduler = scheduler;
            Frame = () =>
            {
                Runs++;
                Body?.Invoke();
                _scheduler.FramePresented();
                if (_running)
                {
                    _scheduler.RequestFrame(Frame!);
                }
            };
        }

        public Action Frame { get; }

        public Action? Body { get; set; }

        public int Runs { get; private set; }

        // Asks for the first frame from outside (it runs at once on the first signal), which asks for the next.
        public void Start()
        {
            _running = true;
            _scheduler.RequestFrame(Frame);
            _scheduler.RunFrame(WAIT.WAIT_OBJECT_0);
        }

        public void Stop() => _running = false;
    }
}
