using System.Diagnostics;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace XylemUI.Platform.Windows.Tests;

// The UI thread's queue (S5): messages from worker threads and await continuations reach the UI thread through one queue that
// the real message loop drains.
[Collection(UiThreadGroup.Name)]
[Trait("Category", "OsTiming")]
public sealed class UiDispatcherTests(UiThreadFixture ui)
{
    private const int Workers = 4;
    private const int MessagesPerWorker = 1000;

    /// <summary>
    /// Messages posted from several worker threads are handled on the UI thread only, in each worker's posting order.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A message handler that records the worker, the sequence number, and whether it runs on the UI thread.
    ///
    /// Steps:
    /// - Run the loop; four threads each post 1,000 numbered messages at the same time.
    /// - Stop when all 4,000 were handled.
    ///
    /// Expected:
    /// - Every message was handled once, all on the UI thread.
    /// - For each worker, the sequence numbers arrived in order.
    /// </remarks>
    [Fact]
    public void Post_FromSeveralWorkers_HandledOnTheUiThreadInEachWorkersOrder() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            var received = new List<Numbered>();
            bool allOnUiThread = true;
            dispatcher.MessageHandler = message =>
            {
                received.Add((Numbered)message);
                allOnUiThread &= UiThread.CheckAccess();
            };

            var workers = new Thread[Workers];
            using var watchdog = new LoopWatchdog();
            try
            {
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => received.Count == Workers * MessagesPerWorker,
                    () =>
                    {
                        for (int w = 0; w < Workers; w++)
                        {
                            int worker = w;
                            workers[w] = new Thread(() =>
                            {
                                for (int i = 0; i < MessagesPerWorker; i++)
                                {
                                    dispatcher.Post(new Numbered(worker, i));
                                }
                            });
                            workers[w].Start();
                        }
                    }
                );
            }
            finally
            {
                foreach (Thread worker in workers)
                {
                    worker?.Join();
                }

                dispatcher.MessageHandler = null;
            }

            Assert.False(watchdog.Expired);
            Assert.True(allOnUiThread);
            Assert.Equal(Workers * MessagesPerWorker, received.Count);
            for (int w = 0; w < Workers; w++)
            {
                int worker = w;
                int[] sequence = received.Where(m => m.Worker == worker).Select(m => m.Sequence).ToArray();
                Assert.Equal(Enumerable.Range(0, MessagesPerWorker), sequence);
            }
        });

    /// <summary>
    /// Messages posted before the loop starts are handled on its first iteration, before it first waits.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Three messages posted on the UI thread before Run.
    ///
    /// Steps:
    /// - Run the loop and record how many were handled when the first iteration ends.
    ///
    /// Expected:
    /// - All three were handled by the end of the first iteration, in order.
    /// </remarks>
    [Fact]
    public void Post_BeforeTheLoopStarts_IsHandledOnTheFirstIteration() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            var received = new List<int>();
            dispatcher.MessageHandler = message => received.Add(((Numbered)message).Sequence);
            int handledAtFirstIteration = -1;
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () =>
                    {
                        handledAtFirstIteration = received.Count;
                        return true;
                    },
                    () =>
                    {
                        dispatcher.Post(new Numbered(0, 0));
                        dispatcher.Post(new Numbered(0, 1));
                        dispatcher.Post(new Numbered(0, 2));
                    }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                dispatcher.MessageHandler = null;
            }

            Assert.Equal(3, handledAtFirstIteration);
            Assert.Equal([0, 1, 2], received);
        });

    /// <summary>
    /// A post from another thread wakes the loop from its blocking wait and is handled at once.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No frame is requested, so the loop blocks without a handle.
    ///
    /// Steps:
    /// - When the loop first waits, another thread waits 300 ms and posts one message.
    /// - Record when the handler ran.
    ///
    /// Expected:
    /// - The message was handled less than 50 ms after the post.
    /// </remarks>
    [Fact]
    public void Post_WhileTheLoopBlocks_WakesItAndIsHandledQuickly()
    {
        ITestOutputHelper? output = TestContext.Current.TestOutputHelper;
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            long postedAt = 0;
            long handledAt = 0;
            dispatcher.MessageHandler = _ => handledAt = Stopwatch.GetTimestamp();
            Thread? poster = null;
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () =>
                    {
                        if (poster is null)
                        {
                            poster = new Thread(() =>
                            {
                                Thread.Sleep(300);
                                Volatile.Write(ref postedAt, Stopwatch.GetTimestamp());
                                dispatcher.Post(new Numbered(0, 0));
                            });
                            poster.Start();
                        }

                        return handledAt != 0;
                    },
                    () => { }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                poster?.Join();
                dispatcher.MessageHandler = null;
            }

            TimeSpan delay = Stopwatch.GetElapsedTime(Volatile.Read(ref postedAt), handledAt);
            output?.WriteLine($"postToHandled={delay.TotalMilliseconds:F3}ms");
            Assert.InRange(delay, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        });
    }

    /// <summary>
    /// After an await, an async method started on the UI thread resumes on the UI thread through the loop's context.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The loop runs; an async method starts on the UI thread from the loop's first iteration.
    ///
    /// Steps:
    /// - The method awaits work on the thread pool, then Task.Yield, and records the thread after each await.
    ///
    /// Expected:
    /// - The synchronization context inside the loop is the dispatcher's.
    /// - Both continuations ran on the UI thread; the thread-pool work did not.
    /// </remarks>
    [Fact]
    public void Await_InsideTheLoop_ResumesOnTheUiThread() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            bool contextIsDispatchers = false;
            bool workOnUiThread = true;
            bool afterTaskRunOnUiThread = false;
            bool afterYieldOnUiThread = false;
            Task? flow = null;

            async Task FlowAsync()
            {
                contextIsDispatchers = SynchronizationContext.Current == dispatcher.SynchronizationContext;
                workOnUiThread = await Task.Run(UiThread.CheckAccess);
                afterTaskRunOnUiThread = UiThread.CheckAccess();
                await Task.Yield();
                afterYieldOnUiThread = UiThread.CheckAccess();
            }

            using var watchdog = new LoopWatchdog();
            LoopTestHelpers.RunLoop(
                watchdog,
                () =>
                {
                    flow ??= FlowAsync();
                    return flow.IsCompleted;
                },
                () => { }
            );

            Assert.False(watchdog.Expired);
            Assert.True(flow!.IsCompletedSuccessfully);
            Assert.True(contextIsDispatchers);
            Assert.False(workOnUiThread);
            Assert.True(afterTaskRunOnUiThread);
            Assert.True(afterYieldOnUiThread);
        });

    /// <summary>
    /// A synchronous render never drains the queue: work posted during it runs after it, from the loop.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A hidden window whose synchronous render posts a message, then calls Drain itself and records the result.
    ///
    /// Steps:
    /// - Run the loop; on its first iteration, resize the window.
    ///
    /// Expected:
    /// - Drain inside the render ran nothing and the message was not handled during the render.
    /// - The message was handled afterwards by the loop.
    /// </remarks>
    [Fact]
    public void Drain_DuringASynchronousRender_RunsNothing() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            using var window = new Win32Window("render", 200, 100);
            int handled = 0;
            int drainedInsideRender = -1;
            int handledInsideRender = -1;
            dispatcher.MessageHandler = _ => handled++;
            window.SynchronousRender = (_, _, _) =>
            {
                dispatcher.Post(new Numbered(0, 0));
                drainedInsideRender = dispatcher.Drain(Win32MessageLoop.CallbackExceptionHandler);
                handledInsideRender = handled;
            };
            bool resized = false;
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () =>
                    {
                        if (!resized)
                        {
                            resized = true;
                            Assert.True(
                                SetWindowPos(window.Hwnd, HWND.NULL, 0, 0, 320, 220, (uint)(SWP.SWP_NOMOVE | SWP.SWP_NOZORDER | SWP.SWP_NOACTIVATE))
                            );
                            return false;
                        }

                        return handled == 1;
                    },
                    () => { }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                dispatcher.MessageHandler = null;
            }

            Assert.Equal(0, drainedInsideRender);
            Assert.Equal(0, handledInsideRender);
            Assert.Equal(1, handled);
        });

    /// <summary>
    /// A handler or continuation that throws is reported, and the messages after it are still handled.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    /// - A message handler that throws for sequence 1.
    ///
    /// Steps:
    /// - Post sequences 0, 1, 2 and run the loop until 2 was handled.
    ///
    /// Expected:
    /// - The handler saw 0, 1, 2; one exception was reported.
    /// </remarks>
    [Fact]
    public void Handler_Throws_IsReportedAndLaterMessagesRun() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            var seen = new List<int>();
            dispatcher.MessageHandler = message =>
            {
                int sequence = ((Numbered)message).Sequence;
                seen.Add(sequence);
                if (sequence == 1)
                {
                    throw new InvalidOperationException("Thrown from a message handler on purpose.");
                }
            };
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => seen.Contains(2),
                    () =>
                    {
                        dispatcher.Post(new Numbered(0, 0));
                        dispatcher.Post(new Numbered(0, 1));
                        dispatcher.Post(new Numbered(0, 2));
                    }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
                dispatcher.MessageHandler = null;
            }

            Assert.Equal([0, 1, 2], seen);
            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
        });

    /// <summary>
    /// Draining off the UI thread throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread is the UI thread; the test runs on another thread.
    ///
    /// Steps:
    /// - Call Drain on the test thread.
    ///
    /// Expected:
    /// - <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public void Drain_OffTheUiThread_Throws()
    {
        UiDispatcher dispatcher = null!;
        ui.Invoke(() => dispatcher = Win32MessageLoop.Dispatcher);

        Assert.Throws<InvalidOperationException>(() => dispatcher.Drain(_ => { }));
    }

    private sealed record Numbered(int Worker, int Sequence) : UiMessage;
}
