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
    /// Messages and await continuations share one queue and run in the order they were queued.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A message handler and continuations that append to one list.
    ///
    /// Steps:
    /// - Before Run, queue message 0, continuation A, message 1, continuation B, alternating Post and SynchronizationContext.Post.
    /// - Run the loop until all four ran.
    ///
    /// Expected:
    /// - The order is message 0, A, message 1, B.
    /// </remarks>
    [Fact]
    public void MessagesAndContinuations_QueuedInTurn_RunInQueueOrder() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            SynchronizationContext context = dispatcher.SynchronizationContext;
            var order = new List<string>();
            dispatcher.MessageHandler = message => order.Add("m" + ((Numbered)message).Sequence);
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => order.Count == 4,
                    () =>
                    {
                        dispatcher.Post(new Numbered(0, 0));
                        context.Post(_ => order.Add("A"), null);
                        dispatcher.Post(new Numbered(0, 1));
                        context.Post(_ => order.Add("B"), null);
                    }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                dispatcher.MessageHandler = null;
            }

            Assert.Equal(["m0", "A", "m1", "B"], order);
        });

    /// <summary>
    /// Work queued while the queue drains waits for the next iteration, so a handler that keeps queuing cannot hold the loop.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A message handler that posts the next message each time, 20 messages in all.
    ///
    /// Steps:
    /// - Post the first message before Run; run the loop and record how many were handled at each iteration.
    ///
    /// Expected:
    /// - One message was handled by the end of the first iteration.
    /// - No iteration handled more than one; the loop took at least 20 iterations.
    /// </remarks>
    [Fact]
    public void Drain_WorkQueuedWhileDraining_WaitsForTheNextIteration() =>
        ui.Invoke(() =>
        {
            const int Total = 20;
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            int handled = 0;
            dispatcher.MessageHandler = _ =>
            {
                handled++;
                if (handled < Total)
                {
                    dispatcher.Post(new Numbered(0, handled));
                }
            };
            var handledAtIteration = new List<int>();
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () =>
                    {
                        handledAtIteration.Add(handled);
                        return handled == Total;
                    },
                    () => dispatcher.Post(new Numbered(0, 0))
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                dispatcher.MessageHandler = null;
            }

            Assert.Equal(1, handledAtIteration[0]);
            for (int i = 1; i < handledAtIteration.Count; i++)
            {
                Assert.InRange(handledAtIteration[i] - handledAtIteration[i - 1], 0, 1);
            }

            Assert.True(handledAtIteration.Count >= Total);
        });

    /// <summary>
    /// A continuation that throws is reported, and the work after it still runs.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    ///
    /// Steps:
    /// - Before Run, post a continuation that throws and one that records that it ran, through the synchronization context.
    /// - Run the loop until the second ran.
    ///
    /// Expected:
    /// - One exception was reported; the second continuation ran.
    /// </remarks>
    [Fact]
    public void Continuation_Throws_IsReportedAndLaterWorkRuns() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            SynchronizationContext context = dispatcher.SynchronizationContext;
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            bool laterRan = false;
            try
            {
                using var watchdog = new LoopWatchdog();
                LoopTestHelpers.RunLoop(
                    watchdog,
                    () => laterRan,
                    () =>
                    {
                        context.Post(_ => throw new InvalidOperationException("Thrown from a continuation on purpose."), null);
                        context.Post(_ => laterRan = true, null);
                    }
                );

                Assert.False(watchdog.Expired);
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }

            Assert.True(laterRan);
            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
        });

    /// <summary>
    /// Send on the UI thread runs the callback at once, without queuing it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The loop is not running.
    ///
    /// Steps:
    /// - Call Send on the UI thread, then Drain.
    ///
    /// Expected:
    /// - The callback had run when Send returned; Drain then found nothing to run.
    /// </remarks>
    [Fact]
    public void Send_OnTheUiThread_RunsAtOnce() =>
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            bool ran = false;

            dispatcher.SynchronizationContext.Send(_ => ran = true, null);

            Assert.True(ran);
            Assert.Equal(0, dispatcher.Drain(Win32MessageLoop.CallbackExceptionHandler));
        });

    /// <summary>
    /// Send from another thread runs the callback on the UI thread and returns only after it ran.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The loop runs; a worker thread starts at the first iteration.
    ///
    /// Steps:
    /// - The worker calls Send with a callback that records its thread and that it ran, then records what it saw on return.
    ///
    /// Expected:
    /// - The callback ran on the UI thread, and it had run when Send returned on the worker.
    /// </remarks>
    [Fact]
    public void Send_FromAnotherThread_RunsOnTheUiThreadAndWaits() =>
        ui.Invoke(() =>
        {
            SynchronizationContext context = Win32MessageLoop.Dispatcher.SynchronizationContext;
            bool ranOnUiThread = false;
            bool ran = false;
            bool ranWhenSendReturned = false;
            bool returned = false;

            RunWorkerDuringLoop(
                () =>
                {
                    context.Send(
                        _ =>
                        {
                            ranOnUiThread = UiThread.CheckAccess();
                            Volatile.Write(ref ran, true);
                        },
                        null
                    );
                    ranWhenSendReturned = Volatile.Read(ref ran);
                    Volatile.Write(ref returned, true);
                },
                () => Volatile.Read(ref returned)
            );

            Assert.True(ranOnUiThread);
            Assert.True(ranWhenSendReturned);
        });

    /// <summary>
    /// Send from another thread throws the callback's exception to the caller, not to the callback exception handler.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A callback exception handler that records what it receives.
    /// - The loop runs; a worker thread starts at the first iteration.
    ///
    /// Steps:
    /// - The worker calls Send with a callback that throws, and catches what Send throws.
    ///
    /// Expected:
    /// - The worker caught the same exception; nothing was reported.
    /// </remarks>
    [Fact]
    public void Send_FromAnotherThreadAndTheCallbackThrows_ThrowsToTheCaller() =>
        ui.Invoke(() =>
        {
            SynchronizationContext context = Win32MessageLoop.Dispatcher.SynchronizationContext;
            Action<Exception> previous = Win32MessageLoop.CallbackExceptionHandler;
            var reported = new List<Exception>();
            Win32MessageLoop.CallbackExceptionHandler = reported.Add;
            var thrown = new InvalidOperationException("Thrown from a Send callback on purpose.");
            Exception? caught = null;
            bool returned = false;
            try
            {
                RunWorkerDuringLoop(
                    () =>
                    {
                        try
                        {
                            context.Send(_ => throw thrown, null);
                        }
                        catch (Exception exception)
                        {
                            caught = exception;
                        }

                        Volatile.Write(ref returned, true);
                    },
                    () => Volatile.Read(ref returned)
                );
            }
            finally
            {
                Win32MessageLoop.CallbackExceptionHandler = previous;
            }

            Assert.Same(thrown, caught);
            Assert.Empty(reported);
        });

    /// <summary>
    /// A message drained with no message handler fails the invariant instead of being dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). No message handler.
    ///
    /// Steps:
    /// - Post a message on the UI thread and drain.
    ///
    /// Expected:
    /// - <see cref="InvariantViolationException"/>.
    /// </remarks>
    [Fact]
    public void Drain_MessageWithNoHandler_FailsTheInvariant()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
        ui.Invoke(() =>
        {
            UiDispatcher dispatcher = Win32MessageLoop.Dispatcher;
            dispatcher.MessageHandler = null;
            dispatcher.Post(new Numbered(0, 0));

            Assert.Throws<InvariantViolationException>(() => dispatcher.Drain(Win32MessageLoop.CallbackExceptionHandler));
        });
    }

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

    // Runs the loop, starts work on a worker thread at the first iteration, and stops when isDone returns true.
    private static void RunWorkerDuringLoop(Action work, Func<bool> isDone)
    {
        Thread? worker = null;
        using var watchdog = new LoopWatchdog();
        try
        {
            LoopTestHelpers.RunLoop(
                watchdog,
                () =>
                {
                    if (worker is null)
                    {
                        // The wake lets the loop see isDone after the worker finished; nothing else wakes it then.
                        worker = new Thread(() =>
                        {
                            work();
                            Win32MessageLoop.Wake();
                        });
                        worker.Start();
                    }

                    return isDone();
                },
                () => { }
            );
        }
        finally
        {
            // A worker blocked in Send after the loop stopped would never return; the bound keeps the run from hanging.
            _ = worker?.Join(TimeSpan.FromSeconds(5));
        }

        Assert.False(watchdog.Expired);
    }

    private sealed record Numbered(int Worker, int Sequence) : UiMessage;
}
