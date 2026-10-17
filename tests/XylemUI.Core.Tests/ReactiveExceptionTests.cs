namespace XylemUI.Tests;

// Stabilization cycles and exceptions in the flush (S6a, design §2.2.2, the batch contract and exceptions).
[Collection(UiThreadGroup.Name)]
public sealed class ReactiveExceptionTests(UiThreadFixture ui)
{
    /// <summary>
    /// A signal set inside an effect runs its readers in the next stabilization cycle of the same flush.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - Signals input and output. Effect Copy reads input and sets output; effect Show reads output. Both record the cycle
    ///   they ran in.
    ///
    /// Steps:
    /// - Set input.
    ///
    /// Expected:
    /// - Show ran once, one cycle after Copy, and saw the new output, before Set returned.
    /// </remarks>
    [Fact]
    public void SetInsideAnEffect_RunsItsReadersInTheNextCycle() =>
        ui.Invoke(() =>
        {
            Signal<int> input = Signal.Create(0);
            Signal<int> output = Signal.Create(0);
            ulong copyCycle = 0;
            ulong showCycle = 0;
            int shown = -1;
            int showRuns = 0;
            using Effect copy = Effect.Create(() =>
            {
                output.Set(input.Get() * 10);
                copyCycle = ReactiveRuntime.CycleCount;
            });
            using Effect show = Effect.Create(() =>
            {
                shown = output.Get();
                showCycle = ReactiveRuntime.CycleCount;
                showRuns++;
            });
            showRuns = 0;

            input.Set(2);

            Assert.Equal(1, showRuns);
            Assert.Equal(20, shown);
            Assert.Equal(copyCycle + 1, showCycle);
        });

    /// <summary>
    /// An effect that keeps setting what it reads stops at the cycle limit, and the handler receives the diagnostic.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. An effect that reads a counter and sets it to the next value, and counts its
    ///   runs.
    ///
    /// Steps:
    /// - Set the counter once.
    ///
    /// Expected:
    /// - Set returned; the effect ran once per cycle up to the limit.
    /// - The handler received one <see cref="ReactiveStabilizationException"/>.
    /// - The effect is still queued for the next flush.
    /// </remarks>
    [Fact]
    public void NonStabilizingEffect_StopsAtTheLimitAndReports() =>
        ui.Invoke(() =>
        {
            Signal<int> counter = Signal.Create(0);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            int runs = 0;
            bool running = false;
            var effect = Effect.Create(() =>
            {
                int value = counter.Get();
                runs++;
                if (running)
                {
                    counter.Set(value + 1);
                }
            });
            try
            {
                running = true;
                runs = 0;

                counter.Set(1);

                Assert.Equal(ReactiveRuntime.MaxFlushIterations, runs);
                Assert.IsType<ReactiveStabilizationException>(Assert.Single(reported));
                Assert.Equal(1, ReactiveRuntime.QueuedCount);
            }
            finally
            {
                effect.Dispose();
                ReactiveRuntime.Batch(() => { });
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.Equal(0, ReactiveRuntime.QueuedCount);
        });

    /// <summary>
    /// An effect that throws is reported, keeps its subscription, and does not stop the other effects of the flush.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that records what it receives. Effect Failing reads a signal and throws when it is 1; effect Other reads the
    ///   same signal.
    ///
    /// Steps:
    /// - Set the signal to 1, then to 2.
    ///
    /// Expected:
    /// - One exception was reported; Other ran for both sets.
    /// - Failing ran again for 2 (its subscription was kept).
    /// </remarks>
    [Fact]
    public void Effect_Throws_IsReportedAndStaysSubscribed() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            var reported = new List<Exception>();
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = reported.Add;
            var failingSaw = new List<int>();
            int otherRuns = 0;
            try
            {
                using Effect failing = Effect.Create(() =>
                {
                    int value = source.Get();
                    failingSaw.Add(value);
                    if (value == 1)
                    {
                        throw new InvalidOperationException("Thrown from an effect on purpose.");
                    }
                });
                using Effect other = Effect.Create(() =>
                {
                    _ = source.Get();
                    otherRuns++;
                });

                source.Set(1);
                source.Set(2);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.IsType<InvalidOperationException>(Assert.Single(reported));
            Assert.Equal([0, 1, 2], failingSaw);
            Assert.Equal(3, otherRuns);
        });

    /// <summary>
    /// A handler that throws does not stop the flush.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A handler that throws. Two effects read a signal; the first throws when the signal is 1.
    ///
    /// Steps:
    /// - Set the signal to 1.
    ///
    /// Expected:
    /// - Set returned normally and the second effect ran.
    /// </remarks>
    [Fact]
    public void Handler_Throws_IsDroppedAndTheFlushGoesOn() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(0);
            Action<Exception> previous = ReactiveRuntime.UnhandledExceptionHandler;
            ReactiveRuntime.UnhandledExceptionHandler = _ => throw new InvalidOperationException("Thrown from the handler on purpose.");
            int secondRuns = 0;
            try
            {
                using Effect first = Effect.Create(() =>
                {
                    if (source.Get() == 1)
                    {
                        throw new InvalidOperationException("Thrown from an effect on purpose.");
                    }
                });
                using Effect second = Effect.Create(() =>
                {
                    _ = source.Get();
                    secondRuns++;
                });

                source.Set(1);
            }
            finally
            {
                ReactiveRuntime.UnhandledExceptionHandler = previous;
            }

            Assert.Equal(2, secondRuns);
        });

    /// <summary>
    /// A computed value that throws keeps the exception and throws it again on each read, until a source changes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A computed value of a signal that throws for negative values and counts its evaluations.
    ///
    /// Steps:
    /// - Set the signal to -1 and read the computed value twice; set it to 4 and read again.
    ///
    /// Expected:
    /// - Both reads throw the same exception instance; the computation ran once for them.
    /// - After the set to 4, the read returns 4.
    /// </remarks>
    [Fact]
    public void Computed_Throws_GetThrowsAgainUntilASourceChanges() =>
        ui.Invoke(() =>
        {
            Signal<int> source = Signal.Create(1);
            int evaluations = 0;
            Computed<int> checkedValue = Computed.Create(() =>
            {
                evaluations++;
                int value = source.Get();
                return value < 0 ? throw new ArgumentOutOfRangeException(nameof(value), "Negative on purpose.") : value;
            });
            Assert.Equal(1, checkedValue.Get());

            source.Set(-1);
            ArgumentOutOfRangeException first = Assert.Throws<ArgumentOutOfRangeException>(() => checkedValue.Get());
            ArgumentOutOfRangeException second = Assert.Throws<ArgumentOutOfRangeException>(() => checkedValue.Get());
            int evaluationsWhileFailing = evaluations;

            source.Set(4);

            Assert.Same(first, second);
            Assert.Equal(2, evaluationsWhileFailing);
            Assert.Equal(4, checkedValue.Get());
        });
}
