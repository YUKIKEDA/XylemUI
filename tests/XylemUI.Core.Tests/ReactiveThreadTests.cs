namespace XylemUI.Tests;

// The reactive runtime and the UI thread (S6a, design §2.2.1, §2.12): the implicit batch around queued work, and the rejection
// of other threads.
[Collection(UiThreadGroup.Name)]
public sealed class ReactiveThreadTests(UiThreadFixture ui)
{
    /// <summary>
    /// A continuation or a message drained from the UI thread's queue is one batch: however many signals it sets, an effect
    /// that reads them runs once, after it.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An effect that reads two signals and counts its runs. A dispatcher whose wake does nothing.
    ///
    /// Steps:
    /// - Post a continuation through the dispatcher's synchronization context that sets both signals and records the runs.
    /// - Post a message whose handler sets both signals again.
    /// - Drain.
    ///
    /// Expected:
    /// - Inside the continuation the effect had not run again.
    /// - The effect ran once per item: two more runs in all.
    /// </remarks>
    [Fact]
    public void DrainedWork_SettingSeveralSignals_RunsTheEffectOncePerItem() =>
        ui.Invoke(() =>
        {
            Signal<int> first = Signal.Create(0);
            Signal<int> second = Signal.Create(0);
            int runs = 0;
            using Effect effect = Effect.Create(() =>
            {
                _ = first.Get() + second.Get();
                runs++;
            });
            var dispatcher = new UiDispatcher(() => { });
            dispatcher.MessageHandler = _ =>
            {
                first.Set(10);
                second.Set(20);
            };
            int runsInsideContinuation = -1;

            dispatcher.SynchronizationContext.Post(
                _ =>
                {
                    first.Set(1);
                    second.Set(2);
                    runsInsideContinuation = runs;
                },
                null
            );
            dispatcher.Post(new Ping());
            int drained = dispatcher.Drain(exception => throw exception);

            Assert.Equal(2, drained);
            Assert.Equal(1, runsInsideContinuation);
            Assert.Equal(3, runs);
        });

    /// <summary>
    /// The reactive API rejects every thread but the UI thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - A signal and a computed value created on the UI thread.
    ///
    /// Steps:
    /// - On the test thread: Get and Set the signal, Get the computed value, create a signal, an effect, a computed value, and
    ///   run a batch.
    ///
    /// Expected:
    /// - Each throws <see cref="InvalidOperationException"/>, and the signal kept its value.
    /// </remarks>
    [Fact]
    public void ReactiveApi_OffTheUiThread_Throws()
    {
        Signal<int> signal = null!;
        Computed<int> computed = null!;
        ui.Invoke(() =>
        {
            signal = Signal.Create(1);
            computed = Computed.Create(() => signal.Get() + 1);
        });

        Assert.Throws<InvalidOperationException>(() => signal.Get());
        Assert.Throws<InvalidOperationException>(() => signal.Set(2));
        Assert.Throws<InvalidOperationException>(() => computed.Get());
        Assert.Throws<InvalidOperationException>(() => Signal.Create(0));
        Assert.Throws<InvalidOperationException>(() => Computed.Create(() => 0));
        Assert.Throws<InvalidOperationException>(() => Effect.Create(() => { }));
        Assert.Throws<InvalidOperationException>(() => ReactiveRuntime.Batch(() => { }));

        int value = 0;
        ui.Invoke(() => value = signal.Get());
        Assert.Equal(1, value);
    }

    private sealed record Ping : UiMessage;
}
