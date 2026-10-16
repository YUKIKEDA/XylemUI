using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// These tests check how the OS wakes the UI thread (S3), so they run the real message loop and wait in real time.
// LoopWatchdog wakes and fails a loop that does not finish within five seconds instead of hanging the test run.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "OsTiming")]
public sealed unsafe class Win32MessageLoopTests(UiThreadFixture ui)
{
    private const uint ProbeMessage = WM.WM_USER + 2;

    /// <summary>
    /// While frames are requested, frame callbacks and posted messages both make progress.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose hook counts a probe message.
    /// - A frame callback that posts one probe message and requests the next frame, ten times.
    ///
    /// Steps:
    /// - Request the first frame and run the loop until ten frames ran and ten probes were handled.
    ///
    /// Expected:
    /// - The loop finishes before the watchdog; ten frames ran and ten probes were handled.
    /// </remarks>
    [Fact]
    public void Run_WhileFramesAreRequested_HandlesFramesAndMessages() =>
        ui.Invoke(() =>
        {
            using var window = new Win32Window("frames", 200, 100);
            int frames = 0;
            int messages = 0;
            window.MessageHook = (_, message) =>
            {
                if (message != ProbeMessage)
                {
                    return false;
                }

                messages++;
                return true;
            };

            Action? onFrame = null;
            onFrame = () =>
            {
                frames++;
                _ = PostMessageW(window.Hwnd, ProbeMessage, default, default);
                if (frames < 10)
                {
                    Win32MessageLoop.FrameScheduler.RequestFrame(onFrame!);
                }
            };

            using var watchdog = new LoopWatchdog();
            RunLoop(watchdog, () => frames >= 10 && messages >= 10, () => Win32MessageLoop.FrameScheduler.RequestFrame(onFrame));

            Assert.False(watchdog.Expired);
            Assert.Equal(10, frames);
            Assert.Equal(10, messages);
        });

    /// <summary>
    /// With no frame requested, the loop blocks without using CPU, and a wake from another thread resumes it quickly.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No frame is requested.
    ///
    /// Steps:
    /// - When the loop first waits, record the UI thread's CPU time; another thread waits 300 ms and calls Wake.
    /// - When the loop wakes, record the CPU time again and the delay since Wake.
    ///
    /// Expected:
    /// - The loop iterated exactly twice: one wait, one wake.
    /// - The UI thread used less than 10 ms of CPU while it waited.
    /// - The loop woke less than 50 ms after Wake.
    /// </remarks>
    [Fact]
    public void Run_Idle_BlocksWithoutCpuAndWakesFromAnotherThread()
    {
        // TestContext.Current is async-local and does not flow to the fixture's UI thread; read it here.
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            int iterations = 0;
            long cpuAtWait = 0;
            long cpuAtWake = 0;
            long wakeCalledAt = 0;
            long wokeAt = 0;
            Thread? waker = null;

            using var watchdog = new LoopWatchdog();
            Win32MessageLoop.IterationHook = () =>
            {
                iterations++;
                if (iterations == 1)
                {
                    cpuAtWait = CurrentThreadCpuTime100ns();
                    waker = new Thread(() =>
                    {
                        Thread.Sleep(300);
                        Volatile.Write(ref wakeCalledAt, Stopwatch.GetTimestamp());
                        Win32MessageLoop.Wake();
                    });
                    waker.Start();
                }
                else
                {
                    wokeAt = Stopwatch.GetTimestamp();
                    cpuAtWake = CurrentThreadCpuTime100ns();
                    Win32MessageLoop.Quit();
                }

                if (watchdog.Expired)
                {
                    Win32MessageLoop.Quit();
                }
            };

            try
            {
                _ = Win32MessageLoop.Run();
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                waker?.Join();
            }

            TimeSpan cpuWhileWaiting = TimeSpan.FromTicks(cpuAtWake - cpuAtWait);
            TimeSpan wakeDelay = Stopwatch.GetElapsedTime(Volatile.Read(ref wakeCalledAt), wokeAt);
            output?.WriteLine(
                $"iterations={iterations} cpuWhileWaiting={cpuWhileWaiting.TotalMilliseconds:F3}ms wakeDelay={wakeDelay.TotalMilliseconds:F3}ms"
            );

            Assert.False(watchdog.Expired);
            Assert.Equal(2, iterations);
            Assert.InRange(cpuWhileWaiting, TimeSpan.Zero, TimeSpan.FromMilliseconds(10));
            Assert.InRange(wakeDelay, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        });
    }

    /// <summary>
    /// A frame callback runs once on the UI thread, and a cancelled request never runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two callbacks that count their runs and record whether they ran on the UI thread.
    ///
    /// Steps:
    /// - Request the first callback. Request the second and cancel it at once, then request the first again and let it run.
    /// - Wait about 200 ms (more than ten frame periods) and quit.
    ///
    /// Expected:
    /// - The first callback ran exactly once (the second request replaced nothing that was still pending), on the UI thread.
    /// - The cancelled callback never ran.
    /// </remarks>
    [Fact]
    public void RequestFrame_RunsOnceOnTheUiThread_AndCancelFrameDropsTheRequest() =>
        ui.Invoke(() =>
        {
            int runs = 0;
            int cancelledRuns = 0;
            bool onUiThread = false;
            Action callback = () =>
            {
                runs++;
                onUiThread = UiThread.CheckAccess();
            };
            Action cancelled = () => cancelledRuns++;

            IFrameScheduler frames = Win32MessageLoop.FrameScheduler;
            frames.RequestFrame(cancelled);
            frames.CancelFrame();

            using var watchdog = new LoopWatchdog();
            long started = Stopwatch.GetTimestamp();
            RunLoop(
                watchdog,
                () =>
                {
                    // Keep the loop alive for 200 ms with wakes, so that a frame clock that kept running would show up.
                    if (Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(200))
                    {
                        WakeLater(10);
                        return false;
                    }

                    return true;
                },
                () => frames.RequestFrame(callback)
            );

            Assert.False(watchdog.Expired);
            Assert.Equal(1, runs);
            Assert.True(onUiThread);
            Assert.Equal(0, cancelledRuns);
        });

    /// <summary>
    /// Requesting a frame off the UI thread throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread is the UI thread; the scheduler was created there.
    ///
    /// Steps:
    /// - Call RequestFrame on the test thread.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void RequestFrame_OffTheUiThread_Throws()
    {
        IFrameScheduler frames = null!;
        ui.Invoke(() => frames = Win32MessageLoop.FrameScheduler);

        Assert.Throws<InvalidOperationException>(() => frames.RequestFrame(() => { }));
    }

    /// <summary>
    /// A steady run of frames allocates nothing in the loop or the scheduler.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A frame callback created once that requests the next frame.
    ///
    /// Steps:
    /// - Run 70 frames; read the UI thread's allocated bytes at frame 10 and at frame 70.
    ///
    /// Expected:
    /// - The two readings are equal.
    /// </remarks>
    [Fact]
    public void Run_SteadyFrames_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            int frames = 0;
            long atFrame10 = 0;
            long atFrame70 = 0;
            Action? onFrame = null;
            onFrame = () =>
            {
                frames++;
                if (frames == 10)
                {
                    atFrame10 = GC.GetAllocatedBytesForCurrentThread();
                }

                if (frames == 70)
                {
                    atFrame70 = GC.GetAllocatedBytesForCurrentThread();
                    return;
                }

                Win32MessageLoop.FrameScheduler.RequestFrame(onFrame!);
            };

            using var watchdog = new LoopWatchdog();
            RunLoop(watchdog, () => frames >= 70, () => Win32MessageLoop.FrameScheduler.RequestFrame(onFrame));

            Assert.False(watchdog.Expired);
            Assert.Equal(atFrame10, atFrame70);
        });

    // Runs the loop until isDone returns true at an iteration, or the watchdog expires. start runs on the UI thread first.
    private static void RunLoop(LoopWatchdog watchdog, Func<bool> isDone, Action start)
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

    private static void WakeLater(int milliseconds) =>
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(milliseconds);
            Win32MessageLoop.Wake();
        });

    private static long CurrentThreadCpuTime100ns()
    {
        FILETIME creation;
        FILETIME exit;
        FILETIME kernel;
        FILETIME user;
        if (!GetThreadTimes(GetCurrentThread(), &creation, &exit, &kernel, &user))
        {
            throw new InvalidOperationException("GetThreadTimes failed.");
        }

        return ToTicks(kernel) + ToTicks(user);

        static long ToTicks(FILETIME time) => ((long)time.dwHighDateTime << 32) | time.dwLowDateTime;
    }

    // Wakes the loop and marks the test failed if it is still running after the timeout.
    private sealed class LoopWatchdog : IDisposable
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
}
