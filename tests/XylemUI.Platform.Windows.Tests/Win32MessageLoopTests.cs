using System.Diagnostics;
using System.Runtime.InteropServices;
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

    // The messages the UI thread took from its queue (kind 1, WH_GETMESSAGE) or was sent from another thread (kind 2,
    // WH_CALLWNDPROC) while the idle test waited, for its failure message (Issue #126). Hooks of the test's thread only; the loop
    // itself records nothing.
    private static readonly (int Kind, uint Message, nint Window)[] Handled = new (int, uint, nint)[64];
    private static int _handled;

    // The messages the hooks saw, all of them: the array above keeps only the first ones.
    private static int _seen;

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
            LoopTestHelpers.RunLoop(watchdog, () => frames >= 10 && messages >= 10, () => Win32MessageLoop.FrameScheduler.RequestFrame(onFrame));

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
    /// - No iteration without a message happened between the first wait and the Wake call (no polling, no stray wake). An iteration
    ///   after which the hooks saw a message is a wake the operating system's message caused, which the loop must take, and is not
    ///   counted (Issue #126: such wakes failed the test now and then on a busy machine).
    /// - The loop iterated twice, one wait and one wake, and once more for each wake a message caused.
    /// - The UI thread used less than 50 ms of CPU while it waited. GetThreadTimes counts in scheduler ticks (about 15.6 ms), so
    ///   the bound is three ticks: one tick can land on the microseconds before the wait, and 300 ms of polling would still fail.
    /// - The loop woke less than 50 ms after Wake.
    /// - When it woke before Wake, the failure names the messages the UI thread handled while it waited (Issue #126): thread hooks
    ///   of this test record the messages taken from the queue and those sent from another thread.
    /// </remarks>
    [Fact]
    public void Run_Idle_BlocksWithoutCpuAndWakesFromAnotherThread()
    {
        // TestContext.Current is async-local and does not flow to the fixture's UI thread; read it here.
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            int iterations = 0;
            int unexpectedWakes = 0;
            int messageWakes = 0;
            int seenBefore = 0;
            long cpuAtWait = 0;
            long cpuAtWake = 0;
            long wakeCalledAt = 0;
            long wokeAt = 0;
            Thread? waker = null;

            using var watchdog = new LoopWatchdog();
            uint thread = GetCurrentThreadId();
            HHOOK taken = SetWindowsHookExW(WH.WH_GETMESSAGE, &OnMessageTaken, HINSTANCE.NULL, thread);
            HHOOK sent = SetWindowsHookExW(WH.WH_CALLWNDPROC, &OnMessageSent, HINSTANCE.NULL, thread);
            Win32MessageLoop.IterationHook = () =>
            {
                iterations++;
                if (iterations == 1)
                {
                    _handled = 0;
                    _seen = 0;
                    cpuAtWait = CurrentThreadCpuTime100ns();
                    waker = new Thread(() =>
                    {
                        Thread.Sleep(300);
                        Volatile.Write(ref wakeCalledAt, Stopwatch.GetTimestamp());
                        Win32MessageLoop.Wake();
                    });
                    waker.Start();
                }
                else if (Volatile.Read(ref wakeCalledAt) == 0)
                {
                    // Woken before our Wake. With a message the hooks saw since the last iteration, the operating system sent the
                    // thread something (Issue #126): a wake the loop must take. With none, the loop polled, or a wake leaked.
                    if (_seen > seenBefore)
                    {
                        messageWakes++;
                    }
                    else
                    {
                        unexpectedWakes++;
                    }
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

                seenBefore = _seen;
            };

            try
            {
                _ = Win32MessageLoop.Run();
            }
            finally
            {
                Win32MessageLoop.IterationHook = null;
                _ = UnhookWindowsHookEx(taken);
                _ = UnhookWindowsHookEx(sent);
                waker?.Join();
            }

            TimeSpan cpuWhileWaiting = TimeSpan.FromTicks(cpuAtWake - cpuAtWait);
            TimeSpan wakeDelay = Stopwatch.GetElapsedTime(Volatile.Read(ref wakeCalledAt), wokeAt);
            output?.WriteLine(
                $"iterations={iterations} unexpectedWakes={unexpectedWakes} messageWakes={messageWakes} cpuWhileWaiting={cpuWhileWaiting.TotalMilliseconds:F3}ms wakeDelay={wakeDelay.TotalMilliseconds:F3}ms"
            );

            Assert.False(watchdog.Expired);
            Assert.True(unexpectedWakes == 0, $"The loop woke {unexpectedWakes} time(s) before Wake; handled meanwhile: {HandledMessages()}");
            Assert.Equal(2 + messageWakes, iterations);
            Assert.InRange(cpuWhileWaiting, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
            Assert.InRange(wakeDelay, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        });
    }

    /// <summary>
    /// A frame callback runs once on the UI thread, and a cancelled request never runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback that counts its runs and records whether it ran on the UI thread, and a second callback that counts its runs.
    ///
    /// Steps:
    /// - Request the second callback and cancel it at once.
    /// - Request the first callback, then keep the loop iterating for about 200 ms (more than ten frame periods) and quit.
    ///
    /// Expected:
    /// - The first callback ran exactly once, on the UI thread: it did not request again, so no later frame ran it.
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

            // Keep the loop iterating for 200 ms with wakes every 10 ms, so that a callback that ran again would show up.
            // The ticker is stopped and joined before the test ends, so no wake leaks into the next test.
            using (new WakeTicker(TimeSpan.FromMilliseconds(10)))
            {
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(200),
                    () => frames.RequestFrame(callback)
                );
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(1, runs);
            Assert.True(onUiThread);
            Assert.Equal(0, cancelledRuns);
        });

    /// <summary>
    /// The frame clock runs only while a frame is pending: it stops after a frame that requests nothing, and on CancelFrame.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scheduler of the UI thread, with no pending frame.
    ///
    /// Steps:
    /// - Request a frame whose callback requests nothing, and run the loop until it ran.
    /// - Request another frame and cancel it.
    ///
    /// Expected:
    /// - The clock runs while each request is pending.
    /// - The clock is stopped after the frame ran, and after the cancel.
    /// </remarks>
    [Fact]
    public void FrameClock_WithNothingPending_IsStopped() =>
        ui.Invoke(() =>
        {
            var frames = (Win32FrameScheduler)Win32MessageLoop.FrameScheduler;
            int runs = 0;
            bool runningInsideCallback = false;
            Action callback = () =>
            {
                runs++;
                runningInsideCallback = frames.IsClockRunning;
            };

            using var watchdog = new LoopWatchdog();
            bool runningWhilePending = false;
            bool? runningAfterFrame = null;
            LoopTestHelpers.RunLoop(
                watchdog,
                () =>
                {
                    if (runs < 1)
                    {
                        return false;
                    }

                    // Read the clock here, in the iteration after the frame: RunLoop's cleanup cancels the frame and would stop it.
                    runningAfterFrame = frames.IsClockRunning;
                    return true;
                },
                () =>
                {
                    frames.RequestFrame(callback);
                    runningWhilePending = frames.IsClockRunning;
                }
            );

            Assert.False(watchdog.Expired);
            Assert.True(runningWhilePending);
            Assert.True(runningInsideCallback);
            Assert.False(runningAfterFrame);

            frames.RequestFrame(callback);
            Assert.True(frames.IsClockRunning);
            frames.CancelFrame();
            Assert.False(frames.IsClockRunning);
        });

    /// <summary>
    /// A second request before the frame replaces the first: only the last callback runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Two callbacks that count their runs.
    ///
    /// Steps:
    /// - Request the first, then the second, and run the loop until a frame ran; keep it iterating for another 100 ms.
    ///
    /// Expected:
    /// - The second callback ran once; the first never ran.
    /// </remarks>
    [Fact]
    public void RequestFrame_TwiceBeforeTheFrame_RunsOnlyTheLastCallback() =>
        ui.Invoke(() =>
        {
            int firstRuns = 0;
            int secondRuns = 0;
            Action first = () => firstRuns++;
            Action second = () => secondRuns++;
            IFrameScheduler frames = Win32MessageLoop.FrameScheduler;

            using var watchdog = new LoopWatchdog();
            long started = Stopwatch.GetTimestamp();
            using (new WakeTicker(TimeSpan.FromMilliseconds(10)))
            {
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => secondRuns >= 1 && Stopwatch.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(100),
                    () =>
                    {
                        frames.RequestFrame(first);
                        frames.RequestFrame(second);
                    }
                );
            }

            Assert.False(watchdog.Expired);
            Assert.Equal(0, firstRuns);
            Assert.Equal(1, secondRuns);
        });

    /// <summary>
    /// An exception from a frame callback goes to the callback exception handler, and the loop keeps running frames.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    /// - A callback that requests the next frame and then throws, three times.
    ///
    /// Steps:
    /// - Request the first frame and run the loop until three frames ran.
    ///
    /// Expected:
    /// - Three frames ran, so a throwing callback did not stop the loop or drop its own next request.
    /// - The handler received the three exceptions.
    /// </remarks>
    [Fact]
    public void RequestFrame_CallbackThrows_ReportsAndKeepsRunningFrames() =>
        ui.Invoke(() =>
        {
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            try
            {
                int runs = 0;
                Action? onFrame = null;
                onFrame = () =>
                {
                    runs++;
                    if (runs < 3)
                    {
                        Win32MessageLoop.FrameScheduler.RequestFrame(onFrame!);
                    }

                    throw new InvalidOperationException($"Thrown from frame {runs} on purpose.");
                };

                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(watchdog, () => runs >= 3, () => Win32MessageLoop.FrameScheduler.RequestFrame(onFrame));

                Assert.False(watchdog.Expired);
                Assert.Equal(3, runs);
                Assert.Equal(3, reported.Count);
                Assert.All(reported, exception => Assert.IsType<InvalidOperationException>(exception));
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }
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
    /// - The bytes are counted with GC.GetAllocatedBytesForCurrentThread, not PerformanceAssert: the loop runs the frames and
    ///   calls back between them, so the bytes are read at frames 10 and 70 inside the callback, where PerformanceAssert cannot
    ///   drive the runs.
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
            LoopTestHelpers.RunLoop(watchdog, () => frames >= 70, () => Win32MessageLoop.FrameScheduler.RequestFrame(onFrame));

            Assert.False(watchdog.Expired);
            Assert.Equal(atFrame10, atFrame70);
        });

    private static string HandledMessages()
    {
        var text = new System.Text.StringBuilder();
        for (int i = 0; i < Math.Min(_handled, Handled.Length); i++)
        {
            (int kind, uint message, nint window) = Handled[i];
            _ = text.Append($"{(kind == 1 ? "taken" : "sent")} 0x{message:X4} to 0x{window:X}; ");
        }

        return _handled == 0 ? "nothing the hooks saw" : text.ToString();
    }

    // The hook callbacks only write to the array (the index stays in range), so they cannot throw a managed exception.
    [UnmanagedCallersOnly]
    private static LRESULT OnMessageTaken(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            _seen++;
            if (_handled < Handled.Length)
            {
                MSG* message = (MSG*)lParam;
                Handled[_handled++] = (1, message->message, message->hwnd);
            }
        }

        return CallNextHookEx(HHOOK.NULL, code, wParam, lParam);
    }

    [UnmanagedCallersOnly]
    private static LRESULT OnMessageSent(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            _seen++;
            if (_handled < Handled.Length)
            {
                CWPSTRUCT* message = (CWPSTRUCT*)lParam;
                Handled[_handled++] = (2, message->message, message->hwnd);
            }
        }

        return CallNextHookEx(HHOOK.NULL, code, wParam, lParam);
    }

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
}
