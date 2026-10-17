namespace XylemUI.Tests;

// UiThread is process-wide state. UiThreadFixture initializes it on its own thread for every test in the group.
[Collection(UiThreadGroup.Name)]
public sealed class UiThreadTests(UiThreadFixture ui)
{
    /// <summary>
    /// The UI-thread check accepts the initializing thread and rejects every other thread.
    /// </summary>
    /// <remarks>
    /// Preconditions:
    /// - The fixture's thread called <see cref="UiThread.Initialize"/>.
    ///
    /// Steps:
    /// - Call <see cref="UiThread.VerifyAccess"/> and <see cref="UiThread.Initialize"/> again on that thread.
    /// - Call <see cref="UiThread.VerifyAccess"/> on the test thread and on a thread-pool thread.
    /// - Call <see cref="UiThread.Initialize"/> on the test thread.
    ///
    /// Expected:
    /// - The fixture's thread passes, and a second <see cref="UiThread.Initialize"/> on it does nothing.
    /// - The test thread and the thread-pool thread get <see cref="InvalidOperationException"/>.
    /// - <see cref="UiThread.Initialize"/> from the test thread throws <see cref="InvalidOperationException"/>.
    /// </remarks>
    [Fact]
    public async Task VerifyAccess_OffTheInitializingThread_Throws()
    {
        ui.Invoke(() =>
        {
            UiThread.VerifyAccess();
            UiThread.Initialize();
        });

        Assert.True(UiThread.IsInitialized);
        Assert.False(UiThread.CheckAccess());
        Assert.Throws<InvalidOperationException>(UiThread.VerifyAccess);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(UiThread.VerifyAccess, TestContext.Current.CancellationToken));
        Assert.Throws<InvalidOperationException>(UiThread.Initialize);
    }
}
