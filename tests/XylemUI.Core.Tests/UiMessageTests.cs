using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace XylemUI.Tests;

// The receivers of a UiMessage (U1, design §2.12): added per type with UiMessage.On<T>, owned by the scope current when added,
// handed each message of their type on the UI thread. Each test disposes the scopes it made, since the table of a type is the
// process's.
[Collection(UiThreadGroup.Name)]
public sealed class UiMessageTests(UiThreadFixture ui)
{
    private static readonly Action<Exception> Rethrow = static exception => ExceptionDispatchInfo.Throw(exception);

    /// <summary>
    /// Messages posted on a worker thread reach their receiver on the UI thread, in the order the worker posted them.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. A receiver of <c>Ping</c>, added in a scope on the UI thread, that records each
    ///   sequence number and whether it runs on the UI thread.
    ///
    /// Steps:
    /// - A worker thread posts <c>Ping</c> 0 to 99; the test joins it.
    /// - Drain on the UI thread.
    ///
    /// Expected:
    /// - The drain ran 100 items; the receiver got 0 to 99 in order, each on the UI thread.
    /// </remarks>
    [Fact]
    public void Post_FromAWorkerThread_ReachesTheReceiverOnTheUiThreadInOrder() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var sequences = new List<int>();
            bool allOnUiThread = true;
            ScopeHandle scope = Scope.Create(() =>
                UiMessage.On<Ping>(ping =>
                {
                    sequences.Add(ping.Sequence);
                    allOnUiThread &= UiThread.CheckAccess();
                })
            );
            try
            {
                var worker = new Thread(() =>
                {
                    for (int i = 0; i < 100; i++)
                    {
                        dispatcher.Post(new Ping(i));
                    }
                });
                worker.Start();
                worker.Join();

                Assert.Equal(100, dispatcher.Drain(Rethrow));
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal(Enumerable.Range(0, 100), sequences);
            Assert.True(allOnUiThread);
        });

    /// <summary>
    /// Each receiver gets only the messages of its own type, in the order they were queued among the other messages and the
    /// continuations.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. A receiver of <c>Ping</c> and one of <c>Pong</c>, each appending to one log.
    ///
    /// Steps:
    /// - Queue <c>Ping</c> 0, a continuation that appends "c", <c>Pong</c> 0, and <c>Ping</c> 1; drain.
    ///
    /// Expected:
    /// - The log is "ping0", "c", "pong0", "ping1".
    /// </remarks>
    [Fact]
    public void On_TwoMessageTypes_EachReceiverGetsOnlyItsTypeInQueueOrder() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            ScopeHandle scope = Scope.Create(() =>
            {
                UiMessage.On<Ping>(ping => log.Add("ping" + ping.Sequence));
                UiMessage.On<Pong>(pong => log.Add("pong" + pong.Sequence));
            });
            try
            {
                dispatcher.Post(new Ping(0));
                dispatcher.SynchronizationContext.Post(_ => log.Add("c"), null);
                dispatcher.Post(new Pong(0));
                dispatcher.Post(new Ping(1));

                Assert.Equal(4, dispatcher.Drain(Rethrow));
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal(["ping0", "c", "pong0", "ping1"], log);
        });

    /// <summary>
    /// A second receiver of a type is added beside the first: each message goes to both, in the order they were added.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. Receivers "a" and then "b" of <c>Ping</c>, in two scopes, appending to one log.
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and 1; drain.
    ///
    /// Expected:
    /// - The log is "a0", "b0", "a1", "b1".
    /// </remarks>
    [Fact]
    public void On_SecondReceiverOfAType_BothGetEachMessageInTheOrderAdded() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            ScopeHandle first = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("a" + ping.Sequence)));
            ScopeHandle second = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("b" + ping.Sequence)));
            try
            {
                dispatcher.Post(new Ping(0));
                dispatcher.Post(new Ping(1));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(second);
                Scope.Dispose(first);
            }

            Assert.Equal(["a0", "b0", "a1", "b1"], log);
        });

    /// <summary>
    /// Disposing the scope that was current when a receiver was added removes that receiver and no other.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. Receivers "a" and "b" of <c>Ping</c>, each in a scope of its own.
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and drain; dispose the scope of "a"; post <c>Ping</c> 1 and drain.
    ///
    /// Expected:
    /// - The log is "a0", "b0", "b1".
    /// </remarks>
    [Fact]
    public void On_ScopeDisposed_RemovesOnlyThatScopesReceiver() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            ScopeHandle first = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("a" + ping.Sequence)));
            ScopeHandle second = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("b" + ping.Sequence)));
            try
            {
                dispatcher.Post(new Ping(0));
                _ = dispatcher.Drain(Rethrow);
                Scope.Dispose(first);
                dispatcher.Post(new Ping(1));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(second);
                Scope.Dispose(first);
            }

            Assert.Equal(["a0", "b0", "b1"], log);
        });

    /// <summary>
    /// A receiver whose scope was disposed is taken out of its type's table: nothing keeps what it holds.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An object held only weakly by the test, and by a receiver of <c>Ping</c> added in a root scope.
    ///
    /// Steps:
    /// - Dispose the root scope and collect garbage.
    ///
    /// Expected:
    /// - The object was collected: the table of <c>Ping</c> no longer holds the receiver.
    /// </remarks>
    [Fact]
    public void On_ScopeDisposed_TheTableDropsTheReceiver() =>
        ui.Invoke(() =>
        {
            WeakReference weak = AddAndDispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(weak.IsAlive);
        });

    /// <summary>
    /// A receiver added by a cleanup of a scope that is being disposed is not left in its type's table.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A root scope with a cleanup that adds a receiver of <c>Ping</c>; the receiver holds an object that the test holds only
    ///   weakly. Whether the add throws or the disposal removes the receiver at once is not what the test reads.
    ///
    /// Steps:
    /// - Dispose the root scope and collect garbage.
    ///
    /// Expected:
    /// - The object was collected: the table of <c>Ping</c> does not hold the receiver.
    /// </remarks>
    [Fact]
    public void On_InsideACleanupOfTheScopeBeingDisposed_LeavesNoReceiver() =>
        ui.Invoke(() =>
        {
            WeakReference weak = AddInsideACleanupAndDispose();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Assert.False(weak.IsAlive);
        });

    /// <summary>
    /// A receiver added inside an effect belongs to the effect's scope: the effect's next run removes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. An effect, in a scope, that reads a signal and adds a receiver of <c>Ping</c>
    ///   that records the signal's value of that run.
    ///
    /// Steps:
    /// - Set the signal from 0 to 1, so that the effect runs again; post <c>Ping</c> 0 and drain.
    ///
    /// Expected:
    /// - Only the receiver of the second run got the message: the log is 1.
    /// </remarks>
    [Fact]
    public void On_InsideAnEffect_IsRemovedWhenTheEffectRunsAgain() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<int>();
            Signal<int> value = Signal.Create(0);
            ScopeHandle scope = Scope.Create(() =>
                _ = Effect.Create(() =>
                {
                    int seen = value.Get();
                    UiMessage.On<Ping>(_ => log.Add(seen));
                })
            );
            try
            {
                value.Set(1);
                dispatcher.Post(new Ping(0));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal([1], log);
        });

    /// <summary>
    /// A receiver runs with the scope it was added in as the current scope, as an input handler does.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. A receiver of <c>Ping</c> added in scope S, which records <see cref="Scope.Current"/>.
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and drain from the UI thread, outside any scope.
    ///
    /// Expected:
    /// - The recorded scope is S, and right after the drain there is no current scope again.
    /// </remarks>
    [Fact]
    public void Receiver_WhenItRuns_HasItsOwnScopeAsTheCurrentScope() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            ScopeHandle current = default;
            ScopeHandle after = default;
            ScopeHandle scope = Scope.Create(() => UiMessage.On<Ping>(_ => current = Scope.Current));
            try
            {
                dispatcher.Post(new Ping(0));
                _ = dispatcher.Drain(Rethrow);
                after = Scope.Current;
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal(scope, current);
            Assert.Equal(default, after);
        });

    /// <summary>
    /// A receiver added after the last one of its type was removed joins the receivers that are left.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. Receivers "a" and "b" of <c>Ping</c>, each in its own scope, added in that order.
    ///
    /// Steps:
    /// - Dispose the scope of "b"; add a receiver "c" in a third scope; post <c>Ping</c> 0 and drain.
    ///
    /// Expected:
    /// - The log is "a0", "c0".
    /// </remarks>
    [Fact]
    public void On_AfterTheLastReceiverWasRemoved_JoinsTheOnesLeft() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            List<string> log = [];
            ScopeHandle a = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add($"a{ping.Sequence}")));
            ScopeHandle b = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add($"b{ping.Sequence}")));
            Scope.Dispose(b);
            ScopeHandle c = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add($"c{ping.Sequence}")));
            try
            {
                dispatcher.Post(new Ping(0));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(a);
                Scope.Dispose(c);
            }

            Assert.Equal(["a0", "c0"], log);
        });

    /// <summary>
    /// Receivers removed one after another from the front of their type's table are all taken out of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Three receivers of <c>Ping</c>, each in its own scope; the first two each hold an object that the test holds only weakly.
    ///
    /// Steps:
    /// - Dispose the first scope, then the second, and collect garbage while the third receiver is still added.
    ///
    /// Expected:
    /// - Both objects were collected: the table holds neither receiver.
    /// </remarks>
    [Fact]
    public void On_TheFirstTwoScopesDisposedInOrder_TheTableDropsBoth() =>
        ui.Invoke(() =>
        {
            (WeakReference first, WeakReference second, ScopeHandle third) = AddThreeAndDisposeTwo();
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Assert.False(first.IsAlive);
                Assert.False(second.IsAlive);
            }
            finally
            {
                Scope.Dispose(third);
            }
        });

    /// <summary>
    /// A receiver added while a message is handed out gets the messages after it, not that one.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. A receiver "first" of <c>Ping</c> that, for sequence 0, adds a receiver "added".
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and 1; drain once.
    ///
    /// Expected:
    /// - The log is "first0", "first1", "added1".
    /// </remarks>
    [Fact]
    public void On_DuringADelivery_GetsOnlyTheLaterMessages() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            ScopeHandle scope = Scope.Create(() =>
                UiMessage.On<Ping>(ping =>
                {
                    log.Add("first" + ping.Sequence);
                    if (ping.Sequence == 0)
                    {
                        UiMessage.On<Ping>(later => log.Add("added" + later.Sequence));
                    }
                })
            );
            try
            {
                dispatcher.Post(new Ping(0));
                dispatcher.Post(new Ping(1));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal(["first0", "first1", "added1"], log);
        });

    /// <summary>
    /// A receiver that disposes its own scope and a later receiver's while a message is handed out stops both, and the receivers
    /// after them still get the message.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. Receivers "a", "c", "d", "e" of <c>Ping</c>, each in a scope of its own; "c"
    ///   disposes its own scope and then the scope of "d".
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and drain; post <c>Ping</c> 1 and drain.
    ///
    /// Expected:
    /// - The log is "a0", "c0", "e0", "a1", "e1": "d" got neither message, and "e" got both.
    /// </remarks>
    [Fact]
    public void Receiver_DisposingItsOwnAndALaterScope_StopsBothAndTheRestStillReceive() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            ScopeHandle d = default;
            ScopeHandle c = default;
            ScopeHandle a = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("a" + ping.Sequence)));
            c = Scope.Create(() =>
                UiMessage.On<Ping>(ping =>
                {
                    log.Add("c" + ping.Sequence);
                    Scope.Dispose(c);
                    Scope.Dispose(d);
                })
            );
            d = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("d" + ping.Sequence)));
            ScopeHandle e = Scope.Create(() => UiMessage.On<Ping>(ping => log.Add("e" + ping.Sequence)));
            try
            {
                dispatcher.Post(new Ping(0));
                _ = dispatcher.Drain(Rethrow);
                dispatcher.Post(new Ping(1));
                _ = dispatcher.Drain(Rethrow);
            }
            finally
            {
                Scope.Dispose(e);
                Scope.Dispose(d);
                Scope.Dispose(c);
                Scope.Dispose(a);
            }

            Assert.Equal(["a0", "c0", "e0", "a1", "e1"], log);
        });

    /// <summary>
    /// A receiver that throws is reported, and the other receivers of the message and the later messages still run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. Receivers "a" (throws for sequence 0) and "b" of <c>Ping</c>.
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0 and 1; drain with a report that records what it receives.
    ///
    /// Expected:
    /// - The log is "a0", "b0", "a1", "b1"; one <see cref="InvalidOperationException"/> was reported.
    /// </remarks>
    [Fact]
    public void Receiver_Throws_IsReportedAndTheOtherReceiversAndLaterMessagesRun() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            var log = new List<string>();
            var reported = new List<Exception>();
            ScopeHandle scope = Scope.Create(() =>
            {
                UiMessage.On<Ping>(ping =>
                {
                    log.Add("a" + ping.Sequence);
                    if (ping.Sequence == 0)
                    {
                        throw new InvalidOperationException("Thrown from a message receiver on purpose.");
                    }
                });
                UiMessage.On<Ping>(ping => log.Add("b" + ping.Sequence));
            });
            try
            {
                dispatcher.Post(new Ping(0));
                dispatcher.Post(new Ping(1));
                _ = dispatcher.Drain(reported.Add);
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal(["a0", "b0", "a1", "b1"], log);
            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
        });

    /// <summary>
    /// A message whose type never had a receiver fails the invariant when it is drained: with no receiver at all, and with a
    /// receiver of another type only.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Debug build (release ends the process). A dispatcher whose wake does nothing. <c>Unheard</c> is a message type that no
    ///   test adds a receiver of.
    ///
    /// Steps:
    /// - Post <c>Unheard</c> with no receiver; drain.
    /// - Add a receiver of <c>Pong</c>; post <c>Unheard</c>; drain.
    ///
    /// Expected:
    /// - Each drain throws <see cref="InvariantViolationException"/>, the receiver of <c>Pong</c> got nothing, and
    ///   <see cref="UiDispatcher.DroppedMessageCount"/> is 0.
    /// </remarks>
    [Fact]
    public void Drain_MessageWhoseTypeNeverHadAReceiver_FailsTheInvariant()
    {
        RequireDebugBuild();
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            dispatcher.Post(new Unheard());
            Assert.Throws<InvariantViolationException>(() => dispatcher.Drain(Rethrow));

            int pongs = 0;
            ScopeHandle other = Scope.Create(() => UiMessage.On<Pong>(_ => pongs++));
            try
            {
                dispatcher.Post(new Unheard());
                Assert.Throws<InvariantViolationException>(() => dispatcher.Drain(Rethrow));
            }
            finally
            {
                Scope.Dispose(other);
            }

            Assert.Equal(0, pongs);
            Assert.Equal(0, dispatcher.DroppedMessageCount);
        });
    }

    /// <summary>
    /// A message drained after every receiver of its type was removed is dropped and counted, and a message that a receiver takes
    /// is not counted.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing.
    ///
    /// Steps:
    /// - Add a receiver of <c>Ping</c> in a scope and dispose the scope; post <c>Ping</c> 0; drain.
    /// - Add a receiver of <c>Ping</c> in a second scope; post <c>Ping</c> 1; drain.
    ///
    /// Expected:
    /// - The first drain runs 1 item and throws nothing; <see cref="UiDispatcher.DroppedMessageCount"/> is 1.
    /// - The second receiver gets 1 only, and the count stays 1.
    /// </remarks>
    [Fact]
    public void Drain_MessageAfterItsReceiversAreGone_IsDroppedAndCounted() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            ScopeHandle gone = Scope.Create(() => UiMessage.On<Ping>(_ => { }));
            Scope.Dispose(gone);
            dispatcher.Post(new Ping(0));

            Assert.Equal(1, dispatcher.Drain(Rethrow));
            Assert.Equal(1, dispatcher.DroppedMessageCount);

            List<int> received = [];
            ScopeHandle scope = Scope.Create(() => UiMessage.On<Ping>(ping => received.Add(ping.Sequence)));
            try
            {
                dispatcher.Post(new Ping(1));
                Assert.Equal(1, dispatcher.Drain(Rethrow));
            }
            finally
            {
                Scope.Dispose(scope);
            }

            Assert.Equal([1], received);
            Assert.Equal(1, dispatcher.DroppedMessageCount);
        });

    /// <summary>
    /// A receiver whose scope is being disposed gets no message, and a message that it alone would have taken is dropped.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing. In scope S, a receiver of <c>Ping</c> and then a cleanup that drains, so that the
    ///   cleanup runs first, while S is being disposed and the receiver is still added.
    ///
    /// Steps:
    /// - Post <c>Ping</c> 0; dispose S.
    ///
    /// Expected:
    /// - The drain inside the cleanup threw nothing; the receiver got nothing, and
    ///   <see cref="UiDispatcher.DroppedMessageCount"/> is 1.
    /// </remarks>
    [Fact]
    public void Drain_OnlyReceiverBeingDisposed_GetsNothingAndTheMessageIsDropped() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            int received = 0;
            Exception? thrown = null;
            ScopeHandle scope = Scope.Create(() =>
            {
                UiMessage.On<Ping>(_ => received++);
                Scope.OnCleanup(() => thrown = Record.Exception(() => dispatcher.Drain(Rethrow)));
            });
            dispatcher.Post(new Ping(0));

            Scope.Dispose(scope);

            Assert.Null(thrown);
            Assert.Equal(0, received);
            Assert.Equal(1, dispatcher.DroppedMessageCount);
        });

    /// <summary>
    /// A receiver is added on the UI thread only, inside a scope, and not while a computed value computes; a refused receiver is
    /// not left behind.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread is the UI thread; the test runs on another thread.
    ///
    /// Steps:
    /// - On the test thread, add a receiver of <c>Ping</c>.
    /// - On the UI thread outside any scope, add one.
    /// - On the UI thread, in a scope, read a computed value whose computation adds one of <c>Refused</c>; then post <c>Refused</c> and drain.
    ///
    /// Expected:
    /// - Each add throws <see cref="InvalidOperationException"/>.
    /// - In a Debug build the drain fails the invariant: the refused receiver was never added.
    /// </remarks>
    [Fact]
    public void On_OffTheUiThreadOutsideAScopeOrInsideAComputed_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => UiMessage.On<Ping>(_ => { }));
        ui.Invoke(() =>
        {
            Assert.Throws<InvalidOperationException>(() => UiMessage.On<Ping>(_ => { }));

            Exception? thrown = null;
            ScopeHandle scope = Scope.Create(() =>
            {
                using Computed<int> computed = Computed.Create(() =>
                {
                    UiMessage.On<Refused>(_ => { });
                    return 0;
                });
                thrown = Record.Exception(() => computed.Get());
            });
            try
            {
                Assert.IsType<InvalidOperationException>(thrown);
#if DEBUG
                var dispatcher = new UiDispatcher(static () => { });
                dispatcher.Post(new Refused());
                Assert.Throws<InvariantViolationException>(() => dispatcher.Drain(Rethrow));
#endif
            }
            finally
            {
                Scope.Dispose(scope);
            }
        });
    }

    /// <summary>
    /// A message posted through a variable of a base type is refused on the posting thread and queues nothing.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing.
    ///
    /// Steps:
    /// - Post a <c>Ping</c> as <see cref="UiMessage"/>, and a <c>Derived</c> as its base record <c>Base</c>; drain.
    ///
    /// Expected:
    /// - Both posts throw <see cref="ArgumentException"/>; the drain runs nothing.
    /// </remarks>
    [Fact]
    public void Post_AsABaseType_ThrowsAndQueuesNothing() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });

            Assert.Throws<ArgumentException>(() => dispatcher.Post<UiMessage>(new Ping(0)));
            Assert.Throws<ArgumentException>(() => dispatcher.Post<Base>(new Derived()));
            Assert.Equal(0, dispatcher.Drain(Rethrow));
        });

    /// <summary>
    /// Posting a message and handing it to its receiver allocates nothing in steady state.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A dispatcher whose wake does nothing; a receiver of <c>Ping</c>, in a scope, that counts; one <c>Ping</c> made before.
    ///   Warmed up with 100 posts and drains.
    ///
    /// Steps:
    /// - 1,000 iterations in the windows of <c>PerformanceAssert.DoesNotAllocate</c>, each posting the same <c>Ping</c> and
    ///   draining.
    ///
    /// Expected:
    /// - 0 bytes in every window; the receiver counted the warm-up's 100 and every run the helper returns.
    /// </remarks>
    [Fact]
    public void SteadyState_PostAndDrain_AllocatesNothing() =>
        ui.Invoke(() =>
        {
            var dispatcher = new UiDispatcher(static () => { });
            int received = 0;
            ScopeHandle scope = Scope.Create(() => UiMessage.On<Ping>(_ => received++));
            try
            {
                var ping = new Ping(0);
                Action<int> iteration = _ =>
                {
                    dispatcher.Post(ping);
                    _ = dispatcher.Drain(Rethrow);
                };

                for (int i = 0; i < 100; i++)
                {
                    iteration(i);
                }

                int runs = PerformanceAssert.DoesNotAllocate(1_000, iteration);

                Assert.Equal(100 + runs, received);
            }
            finally
            {
                Scope.Dispose(scope);
            }
        });

    // Not inlined: inlined into the test, the held object could stay alive in the caller's frame until after the collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddAndDispose()
    {
        object held = new();
        ScopeHandle root = Scope.Create(() => UiMessage.On<Ping>(_ => GC.KeepAlive(held)));
        Scope.Dispose(root);
        return new WeakReference(held);
    }

    // Not inlined: inlined into the test, the held object could stay alive in the caller's frame until after the collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddInsideACleanupAndDispose()
    {
        object held = new();
        ScopeHandle root = Scope.Create(() => Scope.OnCleanup(() => _ = Record.Exception(() => UiMessage.On<Ping>(_ => GC.KeepAlive(held)))));
        Scope.Dispose(root);
        return new WeakReference(held);
    }

    // Not inlined: inlined into the test, the held object could stay alive in the caller's frame until after the collection.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference First, WeakReference Second, ScopeHandle Third) AddThreeAndDisposeTwo()
    {
        object heldByFirst = new();
        object heldBySecond = new();
        ScopeHandle first = Scope.Create(() => UiMessage.On<Ping>(_ => GC.KeepAlive(heldByFirst)));
        ScopeHandle second = Scope.Create(() => UiMessage.On<Ping>(_ => GC.KeepAlive(heldBySecond)));
        ScopeHandle third = Scope.Create(() => UiMessage.On<Ping>(_ => { }));
        Scope.Dispose(first);
        Scope.Dispose(second);
        return (new WeakReference(heldByFirst), new WeakReference(heldBySecond), third);
    }

    private static void RequireDebugBuild()
    {
#if !DEBUG
        Assert.Skip("Invariant.Fail ends the process in release builds; this check runs in the Debug configuration.");
#endif
    }

    private sealed record Ping(int Sequence) : UiMessage;

    private sealed record Pong(int Sequence) : UiMessage;

    // No test adds a receiver of it.
    private sealed record Unheard : UiMessage;

    // The only receiver of it is the one that a computed value refuses.
    private sealed record Refused : UiMessage;

    private record Base : UiMessage;

    private sealed record Derived : Base;
}
