using XylemUI.Testing.XUnit;
using static XylemUI.Elements;

namespace XylemUI.Testing.Tests;

// What a headless app does with what goes wrong (design §12.2, §12.4): options out of range, a root function that throws or returns
// a node it may not, and work on the UI queue that throws.
public sealed class HeadlessAppErrorTests
{
    /// <summary>
    /// Options out of range throw before an app is made.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - None.
    ///
    /// Steps:
    /// - Start an app with a width of 0, a height of infinity, a scale of 0, a frame interval of 0, and at most 0 iterations, one at a
    ///   time.
    ///
    /// Expected:
    /// - Each throws <see cref="ArgumentException"/>.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task StartAsync_OptionsOutOfRange_Throw()
    {
        HeadlessOptions[] bad =
        [
            new() { Width = 0 },
            new() { Height = float.PositiveInfinity },
            new() { Scale = 0 },
            new() { FrameInterval = TimeSpan.Zero },
            new() { MaxIterations = 0 },
        ];
        foreach (HeadlessOptions options in bad)
        {
            _ = await Assert.ThrowsAsync<ArgumentException>(() => HeadlessApp.StartAsync(() => Box(), options));
        }
    }

    /// <summary>
    /// A root function that returns no node or a node with a parent, or that throws, fails the start, and leaves the scope tracker
    /// as it was, so the next app's leak check sees none of the failed app's scopes.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The scope tracker off.
    ///
    /// Steps:
    /// - Start an app whose root function returns the default element; one that returns a box inside a column; one that throws.
    ///   Read the tracker after each. Then start an app of a box and check it for leaks.
    ///
    /// Expected:
    /// - The first two throw <see cref="InvalidOperationException"/>, the third its own exception; the tracker is off after each;
    ///   the last app's check passes.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task StartAsync_TheRootFunctionFails_ThrowsAndLeavesTheTrackerAsItWas()
    {
        bool was = ScopeTracker.IsEnabled;
        ScopeTracker.IsEnabled = false;
        try
        {
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => HeadlessApp.StartAsync(() => default));
            Assert.False(ScopeTracker.IsEnabled);

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                HeadlessApp.StartAsync(() =>
                {
                    Element inner = Box();
                    _ = Column(inner);
                    return inner;
                })
            );
            Assert.False(ScopeTracker.IsEnabled);

            var thrown = new FormatException("the root function's");
            Assert.Same(thrown, await Assert.ThrowsAsync<FormatException>(() => HeadlessApp.StartAsync(() => throw thrown)));
            Assert.False(ScopeTracker.IsEnabled);

            await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
            app.AssertNoLeakedScopes();
        }
        finally
        {
            ScopeTracker.IsEnabled = was;
        }
    }

    /// <summary>
    /// Work on the UI queue that throws fails the pump that runs it: one exception as it is, two together.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - An app of a box, settled.
    ///
    /// Steps:
    /// - Post one callback that throws; pump. Post two that throw; pump. Pump again.
    ///
    /// Expected:
    /// - The first pump throws the callback's exception; the second throws <see cref="AggregateException"/> with the two; the third
    ///   runs nothing and throws nothing.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [UiFact]
    public async Task PumpAndSettle_QueuedWorkThrows_ThrowsItsExceptions()
    {
        await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
        SynchronizationContext context = SynchronizationContext.Current!;
        var one = new FormatException("one");
        context.Post(_ => throw one, null);
        Assert.Same(one, Assert.Throws<FormatException>(() => app.PumpAndSettle()));

        context.Post(_ => throw new FormatException("two"), null);
        context.Post(_ => throw new FormatException("three"), null);
        AggregateException both = Assert.Throws<AggregateException>(() => app.PumpAndSettle());
        Assert.Equal(["two", "three"], both.InnerExceptions.Select(e => e.Message));
        Assert.Equal(0, app.PumpAndSettle());
    }
}

// Work that no test awaits throws between two bodies, on the session's own loop; the app that pumps or is disposed next throws it.
// No other test may run beside these, or its app would take the exception.
[CollectionDefinition(nameof(UnobservedWorkTests), DisableParallelization = true)]
[Collection(nameof(UnobservedWorkTests))]
public sealed class UnobservedWorkTests
{
    /// <summary>
    /// An exception of work that ran between two bodies, with no app pumping, is thrown by the next app's pump.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread with nothing on its queue.
    ///
    /// Steps:
    /// - In one body, post a callback that throws, and end. In the next body, start an app and pump.
    ///
    /// Expected:
    /// - The pump throws the callback's exception.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task UnobservedWork_ThrowsInTheNextAppsPump()
    {
        var thrown = new FormatException("no one awaited it");
        await HeadlessSession.Run(() => SynchronizationContext.Current!.Post(_ => throw thrown, null));
        await HeadlessSession.Run(async () =>
        {
            await using HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
            Assert.Same(thrown, Assert.Throws<FormatException>(() => app.PumpAndSettle()));
        });
    }

    /// <summary>
    /// An exception of work that ran between two bodies is thrown by the next app's disposal when that app never pumps.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The UI thread with nothing on its queue.
    ///
    /// Steps:
    /// - In one body, post a callback that throws, and end. In the next body, start an app and dispose it without a pump.
    ///
    /// Expected:
    /// - The disposal throws the callback's exception.
    /// </remarks>
    /// <returns>A task that completes when the test ends.</returns>
    [Fact]
    public async Task UnobservedWork_ThrowsInTheNextAppsDisposal()
    {
        var thrown = new FormatException("no one awaited it either");
        await HeadlessSession.Run(() => SynchronizationContext.Current!.Post(_ => throw thrown, null));
        await HeadlessSession.Run(async () =>
        {
            HeadlessApp app = await HeadlessApp.StartAsync(() => Box());
            Assert.Same(thrown, await Assert.ThrowsAsync<FormatException>(() => app.DisposeAsync().AsTask()));
        });
    }
}
