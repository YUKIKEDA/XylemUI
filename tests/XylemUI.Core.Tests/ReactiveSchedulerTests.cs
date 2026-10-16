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
