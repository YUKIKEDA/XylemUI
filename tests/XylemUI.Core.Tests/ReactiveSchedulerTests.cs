namespace XylemUI.Tests;

// The batch boundary and the flush (S6a, design §2.2.1, §2.2.2). The reactive runtime belongs to the UI thread, so every test
// runs on the fixture's thread.
[Collection(UiThreadGroup.Name)]
public sealed class ReactiveSchedulerTests(UiThreadFixture ui)
{
    /// <summary>
    /// Several sets inside one batch run an effect that reads them once, after the batch.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An effect that reads two signals and counts its runs.
    ///
    /// Steps:
    /// - In one batch, set the first signal twice and the second once; record the run count inside the batch.
    ///
    /// Expected:
    /// - The effect did not run inside the batch.
    /// - It ran once after the batch and saw the last values.
    /// </remarks>
    [Fact]
    public void Batch_SeveralSets_RunTheEffectOnceAfterIt() =>
        ui.Invoke(() =>
        {
            Signal<int> first = Signal.Create(0);
            Signal<int> second = Signal.Create(0);
            int runs = 0;
            (int, int) seen = default;
            using Effect effect = Effect.Create(() =>
            {
                runs++;
                seen = (first.Get(), second.Get());
            });
            int runsInsideBatch = -1;

            ReactiveRuntime.Batch(() =>
            {
                first.Set(1);
                first.Set(2);
                second.Set(3);
                runsInsideBatch = runs;
            });

            Assert.Equal(1, runsInsideBatch);
            Assert.Equal(2, runs);
            Assert.Equal((2, 3), seen);
        });

    /// <summary>
    /// In a diamond A → B, C → D, the computed D is evaluated once per change and never sees a new B with an old C.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - B = A + 1 and C = A + 2, both computed; D = (B, C), computed, records each pair it reads; an effect reads D.
    ///
    /// Steps:
    /// - Set A from 0 to 1, then to 5.
    ///
    /// Expected:
    /// - D was evaluated once for the effect's first run and once for each set.
    /// - Every pair D read has C = B + 1.
    /// </remarks>
    [Fact]
    public void Diamond_ComputedIsEvaluatedOnceAndNeverSeesAMix() =>
        ui.Invoke(() =>
        {
            Signal<int> a = Signal.Create(0);
            Computed<int> b = Computed.Create(() => a.Get() + 1);
            Computed<int> c = Computed.Create(() => a.Get() + 2);
            var pairs = new List<(int B, int C)>();
            Computed<(int, int)> d = Computed.Create(() =>
            {
                (int, int) pair = (b.Get(), c.Get());
                pairs.Add(pair);
                return pair;
            });
            using Effect effect = Effect.Create(() => _ = d.Get());

            a.Set(1);
            a.Set(5);

            Assert.Equal([(1, 2), (2, 3), (6, 7)], pairs);
        });

    /// <summary>
    /// A computed value is lazy: a change marks it dirty without computing it or queuing it, and the next read computes it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A computed value of a signal that counts its evaluations, read once; no effect reads it.
    ///
    /// Steps:
    /// - Set the signal twice; record the count and the scheduler's queue.
    /// - Read the computed value.
    ///
    /// Expected:
    /// - After the sets, no new evaluation and nothing queued.
    /// - The read evaluated it once and returned the new value.
    /// </remarks>
    [Fact]
    public void Computed_AfterAChange_IsComputedOnlyWhenRead() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(1);
            int evaluations = 0;
            Computed<int> doubled = Computed.Create(() =>
            {
                evaluations++;
                return source.Get() * 2;
            });
            Assert.Equal(2, doubled.Get());

            source.Set(2);
            source.Set(3);
            int evaluationsAfterSets = evaluations;
            int queuedAfterSets = ReactiveRuntime.QueuedCount;

            Assert.Equal(6, doubled.Get());
            Assert.Equal(1, evaluationsAfterSets);
            Assert.Equal(0, queuedAfterSets);
            Assert.Equal(2, evaluations);
        });

    /// <summary>
    /// A computed value that recomputes to an equal result does not run the effects that read it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A computed value IsEven of a signal; an effect reads it and counts its runs.
    ///
    /// Steps:
    /// - Set the signal from 0 to 2 (still even), then to 3.
    ///
    /// Expected:
    /// - The set to 2 did not run the effect; the set to 3 did.
    /// </remarks>
    [Fact]
    public void Computed_EqualResult_DoesNotRunItsReaders() =>
        ui.Invoke(() =>
        {
            Signal<int> number = Signal.Create(0);
            Computed<bool> isEven = Computed.Create(() => number.Get() % 2 == 0);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _ = isEven.Get();
                runs++;
            });

            number.Set(2);
            int runsAfterEven = runs;
            number.Set(3);

            Assert.Equal(1, runsAfterEven);
            Assert.Equal(2, runs);
        });

    /// <summary>
    /// Setting a value equal to the current one does nothing: no reader runs and the version stays.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal with a comparer that ignores case; an effect reads it.
    ///
    /// Steps:
    /// - Set "abc" to "ABC", then to "abd".
    ///
    /// Expected:
    /// - The first set kept the version and did not run the effect; the second did both.
    /// </remarks>
    [Fact]
    public void Set_EqualValue_DoesNothing() =>
        ui.Invoke(() =>
        {
            Signal<string> text = Signal.Create("abc", StringComparer.OrdinalIgnoreCase);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _ = text.Get();
                runs++;
            });
            ulong version = text.Version;

            text.Set("ABC");
            Assert.Equal(version, text.Version);
            Assert.Equal(1, runs);
            Assert.Equal("abc", text.Get());

            text.Set("abd");
            Assert.NotEqual(version, text.Version);
            Assert.Equal(2, runs);
        });

    /// <summary>
    /// Effects run in the order of their depth, not the order they were created or queued.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal; a deep effect that reads it through two computed values, created first; a shallow effect that reads it
    ///   directly, created second. Both log their runs.
    ///
    /// Steps:
    /// - Set the signal.
    ///
    /// Expected:
    /// - The shallow effect ran before the deep one.
    /// </remarks>
    [Fact]
    public void Flush_RunsEffectsInDepthOrder() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            Computed<int> once = Computed.Create(() => source.Get() + 1);
            Computed<int> twice = Computed.Create(() => once.Get() + 1);
            var log = new List<string>();
            using Effect deep = Effect.Create(() => log.Add("deep " + twice.Get()));
            using Effect shallow = Effect.Create(() => log.Add("shallow " + source.Get()));
            log.Clear();

            source.Set(1);

            Assert.True(shallow.Depth < deep.Depth);
            Assert.Equal(["shallow 1", "deep 3"], log);
        });

    /// <summary>
    /// A nested batch does not flush; only the outermost one does, also when its code throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An effect that reads a signal and counts its runs.
    ///
    /// Steps:
    /// - In an outer batch, set the signal inside an inner batch and record the runs after the inner batch; then throw.
    ///
    /// Expected:
    /// - The inner batch did not run the effect.
    /// - The exception reached the caller, and the effect had run once after the outer batch.
    /// - A later set outside any batch runs the effect at once (the depth went back to zero).
    /// </remarks>
    [Fact]
    public void NestedBatch_FlushesOnlyAtTheOutermostEnd_EvenWhenItThrows() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _ = source.Get();
                runs++;
            });
            int runsAfterInner = -1;

            Assert.Throws<InvalidOperationException>(() =>
                ReactiveRuntime.Batch(() =>
                {
                    ReactiveRuntime.Batch(() => source.Set(1));
                    runsAfterInner = runs;
                    throw new InvalidOperationException("Thrown inside a batch on purpose.");
                })
            );

            Assert.Equal(1, runsAfterInner);
            Assert.Equal(2, runs);
            source.Set(2);
            Assert.Equal(3, runs);
        });

    /// <summary>
    /// The value-returning batch returns its result and flushes after it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An effect that reads a signal and counts its runs.
    ///
    /// Steps:
    /// - Call the value-returning batch, which sets the signal twice and returns 42.
    ///
    /// Expected:
    /// - The result is 42 and the effect ran once more.
    /// </remarks>
    [Fact]
    public void BatchWithResult_ReturnsTheResultAndFlushesOnce() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _ = source.Get();
                runs++;
            });

            int result = ReactiveRuntime.Batch(() =>
            {
                source.Set(1);
                source.Set(2);
                return 42;
            });

            Assert.Equal(42, result);
            Assert.Equal(2, runs);
        });

    /// <summary>
    /// A disposed effect never runs again and no longer reads its sources.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An effect that reads a signal and counts its runs.
    ///
    /// Steps:
    /// - Dispose it; set the signal.
    ///
    /// Expected:
    /// - The signal has no observers, and the effect did not run again.
    /// </remarks>
    [Fact]
    public void Effect_Disposed_DoesNotRunAgain() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            int runs = 0;
            var effect = Effect.Create(() =>
            {
                _ = source.Get();
                runs++;
            });

            effect.Dispose();
            source.Set(1);

            Assert.Equal(0, source.ObserverCount);
            Assert.Equal(1, runs);
        });

    /// <summary>
    /// An effect created outside a batch that sets what it reads settles before Create returns.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No batch is open. A signal at 0.
    ///
    /// Steps:
    /// - Create an effect that reads the signal and sets it to the next value while it is below 3.
    ///
    /// Expected:
    /// - When Create returns, the signal is 3 and the effect ran four times; nothing is left queued.
    /// </remarks>
    [Fact]
    public void Create_EffectSettingWhatItReads_SettlesBeforeCreateReturns() =>
        ui.Invoke(() =>
        {
            Signal<int> counter = Signal.Create(0);
            int runs = 0;

            using Effect effect = Effect.Create(() =>
            {
                runs++;
                int value = counter.Get();
                if (value < 3)
                {
                    counter.Set(value + 1);
                }
            });

            Assert.Equal(3, counter.Get());
            Assert.Equal(4, runs);
            Assert.Equal(0, ReactiveRuntime.QueuedCount);
        });

    /// <summary>
    /// The signals an effect sets during its first run are flushed after that run, not in the middle of it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - No batch is open. Effect Watcher reads a signal and logs what it saw.
    ///
    /// Steps:
    /// - Create an effect that logs "start", sets the signal, and logs "end".
    ///
    /// Expected:
    /// - The log is: Watcher's first run, start, end, then Watcher with the new value.
    /// </remarks>
    [Fact]
    public void Create_EffectSettingAnotherEffectsSignal_FlushesAfterItsRun() =>
        ui.Invoke(() =>
        {
            Signal<int> shared = Signal.Create(0);
            var log = new List<string>();
            using Effect watcher = Effect.Create(() => log.Add("watcher " + shared.Get()));

            using Effect writer = Effect.Create(() =>
            {
                log.Add("start");
                shared.Set(1);
                log.Add("end");
            });

            Assert.Equal(["watcher 0", "start", "end", "watcher 1"], log);
        });

    /// <summary>
    /// An effect created inside a flush runs at once, and what becomes dirty in that run waits for the next cycle.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A trigger signal and a signal Value at 0. Effect Parent reads the trigger and, the first time it sees 1, creates effect
    ///   Child. Child reads Value, sets it to 1 while it is 0, and records the cycle of each run.
    ///
    /// Steps:
    /// - Set the trigger to 1.
    ///
    /// Expected:
    /// - Child ran twice: first in Parent's cycle, then in the next cycle; Value is 1.
    /// </remarks>
    [Fact]
    public void Create_InsideAFlush_RunsAtOnceAndRequeuesForTheNextCycle() =>
        ui.Invoke(() =>
        {
            Signal<int> trigger = Signal.Create(0);
            Signal<int> value = Signal.Create(0);
            var childCycles = new List<ulong>();
            ulong parentCycle = 0;
            Effect? child = null;
            using Effect parent = Effect.Create(() =>
            {
                if (trigger.Get() == 1 && child is null)
                {
                    parentCycle = ReactiveRuntime.CycleCount;
                    child = Effect.Create(() =>
                    {
                        childCycles.Add(ReactiveRuntime.CycleCount);
                        if (value.Get() == 0)
                        {
                            value.Set(1);
                        }
                    });
                }
            });

            try
            {
                trigger.Set(1);

                Assert.Equal([parentCycle, parentCycle + 1], childCycles);
                Assert.Equal(1, value.Get());
            }
            finally
            {
                child?.Dispose();
            }
        });

    /// <summary>
    /// An effect that disposes itself while it runs is not queued again, even if it made itself dirty.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal at 0. An effect that reads it; once told to, it also sets the signal to the next value (making itself dirty)
    ///   and disposes itself.
    ///
    /// Steps:
    /// - Tell it to, and set the signal to 10. Then set the signal to 20.
    ///
    /// Expected:
    /// - The effect ran twice (creation and the set to 10), not again; nothing is queued; the signal has no observers.
    /// </remarks>
    [Fact]
    public void Effect_DisposingItselfWhileDirty_IsNotQueuedAgain() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            int runs = 0;
            bool disposeNow = false;
            Effect? self = null;
            self = Effect.Create(() =>
            {
                runs++;
                int value = source.Get();
                if (disposeNow)
                {
                    source.Set(value + 1);
                    self!.Dispose();
                }
            });

            disposeNow = true;
            source.Set(10);
            source.Set(20);

            Assert.Equal(2, runs);
            Assert.Equal(0, ReactiveRuntime.QueuedCount);
            Assert.Equal(0, source.ObserverCount);
            Assert.Equal(20, source.Get());
        });

    /// <summary>
    /// An effect disposed while it waits in the queue does not run.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal. Effect Deep reads it through a computed value and counts its runs; effect Shallow reads it directly and,
    ///   when it sees 1, disposes Deep. Shallow runs first (lower depth).
    ///
    /// Steps:
    /// - Set the signal to 1 (both are queued; Shallow disposes Deep before Deep's turn).
    ///
    /// Expected:
    /// - Deep did not run for the set.
    /// </remarks>
    [Fact]
    public void Effect_DisposedWhileQueued_DoesNotRun() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            Computed<int> through = Computed.Create(() => source.Get() + 1);
            int deepRuns = 0;
            var deep = Effect.Create(() =>
            {
                _ = through.Get();
                deepRuns++;
            });
            using Effect shallow = Effect.Create(() =>
            {
                if (source.Get() == 1)
                {
                    deep.Dispose();
                }
            });

            source.Set(1);

            Assert.True(shallow.Depth < deep.Depth);
            Assert.Equal(1, deepRuns);
        });

    /// <summary>
    /// Disposing a computed value does not notify its readers: a reader runs again only when another source changes, and its
    /// read then throws.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. A computed value of signal A; an effect reads the computed value and signal B.
    ///
    /// Steps:
    /// - Dispose the computed value; set A; then set B.
    ///
    /// Expected:
    /// - Setting A did not run the effect (the computed value no longer reads A).
    /// - Setting B ran it, and its read of the computed value threw <see cref="ObjectDisposedException"/>, which was reported.
    /// </remarks>
    [Fact]
    public void Computed_DisposedWhileRead_ReaderThrowsOnItsNextRun() =>
        ui.Invoke(() =>
        {
            Signal<int> a = Signal.Create(0);
            Signal<int> b = Signal.Create(0);
            Computed<int> computed = Computed.Create(() => a.Get() + 1);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            int runs = 0;
            try
            {
                using Effect effect = Effect.Create(() =>
                {
                    runs++;
                    _ = b.Get();
                    _ = computed.Get();
                });

                computed.Dispose();
                a.Set(1);
                int runsAfterA = runs;
                b.Set(1);

                Assert.Equal(1, runsAfterA);
                Assert.Equal(2, runs);
                Assert.IsType<ObjectDisposedException>(Assert.Single(reported));
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }
        });

    /// <summary>
    /// A disposed computed value throws when read and no longer reads its sources.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A computed value of a signal, read once.
    ///
    /// Steps:
    /// - Dispose it and read it.
    ///
    /// Expected:
    /// - The signal has no observers; the read throws <see cref="ObjectDisposedException"/>.
    /// </remarks>
    [Fact]
    public void Computed_Disposed_ThrowsWhenRead() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            Computed<int> computed = Computed.Create(source.Get);
            _ = computed.Get();

            computed.Dispose();

            Assert.Equal(0, source.ObserverCount);
            Assert.Throws<ObjectDisposedException>(() => computed.Get());
        });
}
